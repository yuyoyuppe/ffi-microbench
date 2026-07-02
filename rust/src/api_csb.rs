//! csbindgen surface: hand-rolled `extern "C"` exports replicating the reference app's
//! native-dll conventions exactly (see the conventions doc in the README):
//!   - strings C#→Rust: UTF-16 ptr + len, decoded with String::from_utf16_lossy
//!   - strings Rust→C#: owned null-terminated UTF-8 CString + csb_dealloc_string
//!   - compound payloads: JSON (serde_json), crossing as UTF-8 bytes / CString
//!   - Vec<String> in: array of UTF-16 pointers + lengths (hotkey registration)
//!   - callbacks: bare Cdecl fn pointers, string args freshly UTF-16 encoded per call
//!   - context: opaque pointer around RwLock<Option<Arc<Impl>>>, acquire per call
//!   - fallible calls: bool return (+ optional out CString message)
//! No catch_unwind, no RustCallStatus — matching the incumbent.

use std::collections::HashMap;
use std::ffi::{c_char, CString};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, RwLock};

use crate::types::*;

fn utf16_to_string(ptr: *const u16, len: i32) -> String {
    if ptr.is_null() || len <= 0 {
        return String::new();
    }
    let slice = unsafe { std::slice::from_raw_parts(ptr, len as usize) };
    String::from_utf16_lossy(slice)
}

fn string_to_cstring_raw(s: &str) -> *mut c_char {
    CString::new(s).expect("payload contains no NUL").into_raw()
}

fn str_to_utf16(s: &str) -> Vec<u16> {
    s.encode_utf16().collect()
}

// Case 1
#[unsafe(no_mangle)]
pub extern "C" fn csb_nop() {}

// Case 2
#[unsafe(no_mangle)]
pub extern "C" fn csb_add(a: u64, b: u64) -> u64 {
    a.wrapping_add(b)
}

// Case 3: context pointer pattern (NativeContext replica).
struct CsbCounterImpl {
    value: AtomicU64,
}

pub struct CsbContext(RwLock<Option<Arc<CsbCounterImpl>>>);

impl CsbContext {
    fn acquire(ptr: *const CsbContext) -> Option<Arc<CsbCounterImpl>> {
        let ctx = unsafe { &*ptr };
        ctx.0.read().ok()?.clone()
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn csb_ctx_create() -> *mut CsbContext {
    Box::into_raw(Box::new(CsbContext(RwLock::new(Some(Arc::new(
        CsbCounterImpl {
            value: AtomicU64::new(0),
        },
    ))))))
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_ctx_destroy(ctx: *mut CsbContext) {
    if ctx.is_null() {
        return;
    }
    let ctx = unsafe { &*ctx };
    *ctx.0.write().unwrap() = None;
    // Wrapper intentionally leaked, mirroring destroy_native_context.
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_ctx_add(ctx: *const CsbContext, x: u64) -> u64 {
    if ctx.is_null() {
        return 0;
    }
    let Some(imp) = CsbContext::acquire(ctx) else {
        return 0;
    };
    imp.value.fetch_add(x, Ordering::Relaxed).wrapping_add(x)
}

// Case 4: string in, UTF-16 ptr+len (Rust decodes to owned UTF-8 String).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_take_string_utf16(ptr: *const u16, len: i32) -> u64 {
    let s = utf16_to_string(ptr, len);
    checksum_str(0, &s)
}

// Case 5: string out, owned null-terminated UTF-8 CString.
#[unsafe(no_mangle)]
pub extern "C" fn csb_give_string_utf8(n: u32) -> *mut c_char {
    string_to_cstring_raw(static_string(n as usize))
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_dealloc_string(ptr: *mut c_char) {
    if !ptr.is_null() {
        drop(unsafe { CString::from_raw(ptr) });
    }
}

// Case 6: bytes in (borrowed ptr+len, Rust copies to own it — clipboard set),
// bytes out (Rust-owned buffer + free).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_take_bytes(ptr: *const u8, len: i32) -> u64 {
    if ptr.is_null() || len <= 0 {
        return 0;
    }
    let owned = unsafe { std::slice::from_raw_parts(ptr, len as usize) }.to_vec();
    owned.len() as u64 ^ (*owned.last().unwrap_or(&0) as u64)
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_give_bytes(n: u32, out_len: *mut i32) -> *mut u8 {
    let owned: Box<[u8]> = static_bytes(n as usize).into();
    unsafe { *out_len = owned.len() as i32 };
    Box::into_raw(owned) as *mut u8
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_dealloc_bytes(ptr: *mut u8, len: i32) {
    if !ptr.is_null() && len > 0 {
        drop(unsafe {
            Box::from_raw(std::ptr::slice_from_raw_parts_mut(ptr, len as usize))
        });
    }
}

// Case 7: Vec<String> in — array of UTF-16 pointers + lengths (hotkey pattern).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_take_string_array_utf16(
    ptrs: *const *const u16,
    lens: *const i32,
    count: i32,
) -> u64 {
    if ptrs.is_null() || lens.is_null() || count <= 0 {
        return 0;
    }
    let ptrs = unsafe { std::slice::from_raw_parts(ptrs, count as usize) };
    let lens = unsafe { std::slice::from_raw_parts(lens, count as usize) };
    let mut acc = 0u64;
    for i in 0..count as usize {
        let s = utf16_to_string(ptrs[i], lens[i]);
        acc = checksum_str(acc, &s);
    }
    acc
}

// Case 7 JSON variant (the real incumbent for hotkeys/snippets): one UTF-8 JSON
// buffer in, serde_json parse; JSON CString out.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_take_string_list_json_utf8(ptr: *const u8, len: i32) -> u64 {
    let bytes = unsafe { std::slice::from_raw_parts(ptr, len as usize) };
    let v: Vec<String> = serde_json::from_slice(bytes).expect("valid json");
    v.iter().fold(0, |acc, s| checksum_str(acc, s))
}

#[unsafe(no_mangle)]
pub extern "C" fn csb_give_string_list_json_utf8(count: u32) -> *mut c_char {
    let json = serde_json::to_string(static_string_list(count as usize)).unwrap();
    string_to_cstring_raw(&json)
}

// Case 8: Vec<Record> — incumbent crosses as JSON.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_take_records_json_utf8(ptr: *const u8, len: i32) -> u64 {
    let bytes = unsafe { std::slice::from_raw_parts(ptr, len as usize) };
    let v: Vec<BenchRecord> = serde_json::from_slice(bytes).expect("valid json");
    v.iter()
        .fold(0, |acc, r| checksum_str(acc, &r.title).wrapping_add(r.id as u64))
}

#[unsafe(no_mangle)]
pub extern "C" fn csb_give_records_json_utf8(count: u32) -> *mut c_char {
    let json = serde_json::to_string(static_records(count as usize)).unwrap();
    string_to_cstring_raw(&json)
}

// Case 9: nested/optional record — incumbent crosses as JSON.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_take_request_json_utf8(ptr: *const u8, len: i32) -> u64 {
    let bytes = unsafe { std::slice::from_raw_parts(ptr, len as usize) };
    let r: WindowRequest = serde_json::from_slice(bytes).expect("valid json");
    let mut acc = r.window_ids.iter().map(|&x| x as u64).sum::<u64>();
    if let Some(t) = &r.title {
        acc = checksum_str(acc, t);
    }
    if let Some(p) = &r.pwa {
        acc = checksum_str(acc, &p.app_id);
    }
    acc.wrapping_add(r.timeout_ms as u64)
}

#[unsafe(no_mangle)]
pub extern "C" fn csb_give_request_json_utf8() -> *mut c_char {
    let json = serde_json::to_string(static_request()).unwrap();
    string_to_cstring_raw(&json)
}

// Case 10: HashMap<String, String> — incumbent crosses as JSON.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_take_map_json_utf8(ptr: *const u8, len: i32) -> u64 {
    let bytes = unsafe { std::slice::from_raw_parts(ptr, len as usize) };
    let m: HashMap<String, String> = serde_json::from_slice(bytes).expect("valid json");
    m.iter()
        .fold(0, |acc, (k, v)| checksum_str(checksum_str(acc, k), v))
}

#[unsafe(no_mangle)]
pub extern "C" fn csb_give_map_json_utf8(count: u32) -> *mut c_char {
    let json = serde_json::to_string(static_map(count as usize)).unwrap();
    string_to_cstring_raw(&json)
}

// Cases 11/12: bare Cdecl fn-pointer callbacks (BindingTriggerCallback / LogCallback).
pub type ScalarCb = extern "C" fn(u32);
pub type StringCb = extern "C" fn(*const u16, i32);

static CSB_SCALAR_CB: RwLock<Option<ScalarCb>> = RwLock::new(None);
static CSB_STRING_CB: RwLock<Option<StringCb>> = RwLock::new(None);

#[unsafe(no_mangle)]
pub extern "C" fn csb_register_scalar_callback(cb: ScalarCb) {
    *CSB_SCALAR_CB.write().unwrap() = Some(cb);
}

#[unsafe(no_mangle)]
pub extern "C" fn csb_register_string_callback(cb: StringCb) {
    *CSB_STRING_CB.write().unwrap() = Some(cb);
}

#[unsafe(no_mangle)]
pub extern "C" fn csb_fire_scalar(times: u32) {
    let cb = CSB_SCALAR_CB
        .read()
        .unwrap()
        .expect("scalar callback not registered");
    for i in 0..times {
        cb(i);
    }
}

/// Fresh UTF-16 encode per invocation, exactly like logger.rs str_to_utf16.
#[unsafe(no_mangle)]
pub extern "C" fn csb_fire_string(times: u32, msg_len: u32) {
    let msg = static_string(msg_len as usize);
    let cb = CSB_STRING_CB
        .read()
        .unwrap()
        .expect("string callback not registered");
    for _ in 0..times {
        let utf16 = str_to_utf16(msg);
        cb(utf16.as_ptr(), utf16.len() as i32);
    }
}

// Case 13: bool-style result (+ out CString message variant, confetti_spawn pattern).
#[unsafe(no_mangle)]
pub extern "C" fn csb_try_op(should_fail: bool) -> bool {
    !should_fail
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn csb_try_op_with_msg(
    should_fail: bool,
    error_out: *mut *mut c_char,
) -> bool {
    if should_fail {
        unsafe { *error_out = string_to_cstring_raw("access denied") };
        false
    } else {
        true
    }
}
