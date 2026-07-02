//! Hand-written `*_raw` exports backing the fork's `high_performance_strings`
//! span fast path. For every top-level uniffi function with a direct String
//! argument, the fork's generated C# declares `{ffi_func_name}_raw` taking
//! `byte* + int` (UTF-8) per string arg instead of a RustBuffer — these exports
//! must exist with matching names/signatures. The string is borrowed zero-copy;
//! status is left untouched on success (zeroed status == success on the C# side).

use uniffi::RustCallStatus;

use crate::types::checksum_str;

unsafe fn borrow_utf8<'a>(ptr: *const u8, len: i32) -> &'a str {
    if ptr.is_null() || len <= 0 {
        return "";
    }
    // The caller (generated C#) passes bytes produced by Encoding.UTF8.
    unsafe { std::str::from_utf8_unchecked(std::slice::from_raw_parts(ptr, len as usize)) }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn uniffi_benchffi_fn_func_u_take_string_raw(
    ptr: *const u8,
    len: i32,
    _status: *mut RustCallStatus,
) -> u64 {
    let s = unsafe { borrow_utf8(ptr, len) };
    checksum_str(0, s)
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn uniffi_benchffi_fn_func_u_take_string_list_json_raw(
    ptr: *const u8,
    len: i32,
    _status: *mut RustCallStatus,
) -> u64 {
    let s = unsafe { borrow_utf8(ptr, len) };
    let v: Vec<String> = serde_json::from_str(s).expect("valid json");
    v.iter().fold(0, |acc, s| checksum_str(acc, s))
}
