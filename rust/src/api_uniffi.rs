//! uniffi 0.31 proc-macro surface. Case numbers refer to the benchmark matrix in
//! uniffi_migration_benchmarks.md. take_* functions return a checksum so work
//! cannot be elided; give_* functions clone lazily-built static payloads so the
//! measured cost is the FFI-mandated copy, matching the csbindgen surface.

use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, RwLock};

use crate::types::*;

// Case 1: per-call floor.
#[uniffi::export]
pub fn u_nop() {}

// Case 2: scalars stay register-passed.
#[uniffi::export]
pub fn u_add(a: u64, b: u64) -> u64 {
    a.wrapping_add(b)
}

// Case 3: object method — Arc handle vs raw context pointer.
#[derive(uniffi::Object)]
pub struct UCounter {
    value: AtomicU64,
}

#[uniffi::export]
impl UCounter {
    #[uniffi::constructor]
    pub fn new() -> Arc<Self> {
        Arc::new(Self {
            value: AtomicU64::new(0),
        })
    }

    pub fn add(&self, x: u64) -> u64 {
        self.value.fetch_add(x, Ordering::Relaxed).wrapping_add(x)
    }
}

// Case 4: string in.
#[uniffi::export]
pub fn u_take_string(s: String) -> u64 {
    checksum_str(0, &s)
}

// Case 5: string out.
#[uniffi::export]
pub fn u_give_string(n: u32) -> String {
    static_string(n as usize).to_owned()
}

// Case 6: bytes.
#[uniffi::export]
pub fn u_take_bytes(b: Vec<u8>) -> u64 {
    b.len() as u64 ^ (*b.last().unwrap_or(&0) as u64)
}

#[uniffi::export]
pub fn u_give_bytes(n: u32) -> Vec<u8> {
    static_bytes(n as usize).to_vec()
}

// Case 7: Vec<String>.
#[uniffi::export]
pub fn u_take_string_list(v: Vec<String>) -> u64 {
    v.iter().fold(0, |acc, s| checksum_str(acc, s))
}

#[uniffi::export]
pub fn u_give_string_list(count: u32) -> Vec<String> {
    static_string_list(count as usize).clone()
}

// Case 7 JSON variant: the incumbent path — compound payload crosses as one JSON
// string, serde_json parses on the Rust side (mirrors hotkey/snippet registration).
#[uniffi::export]
pub fn u_take_string_list_json(json: String) -> u64 {
    let v: Vec<String> = serde_json::from_str(&json).expect("valid json");
    v.iter().fold(0, |acc, s| checksum_str(acc, s))
}

// Case 8: Vec<Record>.
#[uniffi::export]
pub fn u_take_records(v: Vec<BenchRecord>) -> u64 {
    v.iter()
        .fold(0, |acc, r| checksum_str(acc, &r.title).wrapping_add(r.id as u64))
}

#[uniffi::export]
pub fn u_give_records(count: u32) -> Vec<BenchRecord> {
    static_records(count as usize).clone()
}

// Case 9: nested/optional record.
#[uniffi::export]
pub fn u_take_request(r: WindowRequest) -> u64 {
    let mut acc = r.window_ids.iter().map(|&x| x as u64).sum::<u64>();
    if let Some(t) = &r.title {
        acc = checksum_str(acc, t);
    }
    if let Some(p) = &r.pwa {
        acc = checksum_str(acc, &p.app_id);
    }
    acc.wrapping_add(r.timeout_ms as u64)
}

#[uniffi::export]
pub fn u_give_request() -> WindowRequest {
    static_request().clone()
}

// Case 10: HashMap<String, String>.
#[uniffi::export]
pub fn u_take_map(m: HashMap<String, String>) -> u64 {
    m.iter()
        .fold(0, |acc, (k, v)| checksum_str(checksum_str(acc, k), v))
}

#[uniffi::export]
pub fn u_give_map(count: u32) -> HashMap<String, String> {
    static_map(count as usize).clone()
}

// Cases 11/12: callbacks. Registered once (mirrors BindingTriggerCallback /
// LogCallback registration at startup), then fired from Rust in a tight loop.
#[uniffi::export(callback_interface)]
pub trait ScalarCallback: Send + Sync {
    fn on_trigger(&self, id: u32);
}

#[uniffi::export(callback_interface)]
pub trait StringCallback: Send + Sync {
    fn on_log(&self, msg: String);
}

static SCALAR_CB: RwLock<Option<Box<dyn ScalarCallback>>> = RwLock::new(None);
static STRING_CB: RwLock<Option<Box<dyn StringCallback>>> = RwLock::new(None);

#[uniffi::export]
pub fn u_register_scalar_callback(cb: Box<dyn ScalarCallback>) {
    *SCALAR_CB.write().unwrap() = Some(cb);
}

#[uniffi::export]
pub fn u_register_string_callback(cb: Box<dyn StringCallback>) {
    *STRING_CB.write().unwrap() = Some(cb);
}

#[uniffi::export]
pub fn u_fire_scalar(times: u32) {
    let guard = SCALAR_CB.read().unwrap();
    let cb = guard.as_ref().expect("scalar callback not registered");
    for i in 0..times {
        cb.on_trigger(i);
    }
}

/// Message is cloned per invocation, matching the csbindgen path which UTF-16
/// encodes a fresh buffer per call (str_to_utf16).
#[uniffi::export]
pub fn u_fire_string(times: u32, msg_len: u32) {
    let msg = static_string(msg_len as usize);
    let guard = STRING_CB.read().unwrap();
    let cb = guard.as_ref().expect("string callback not registered");
    for _ in 0..times {
        cb.on_log(msg.to_owned());
    }
}

// Case 13: error path — uniffi turns Err into a C# exception.
#[derive(Debug, thiserror::Error, uniffi::Error)]
pub enum BenchError {
    #[error("access denied")]
    Denied,
}

#[uniffi::export]
pub fn u_try_op(should_fail: bool) -> Result<(), BenchError> {
    if should_fail {
        Err(BenchError::Denied)
    } else {
        Ok(())
    }
}

/// Case 13 companion: a throwing function WITH a string argument, so the span
/// fast path's error propagation (status check + error lift) is exercised too.
#[uniffi::export]
pub fn u_take_string_checked(s: String, should_fail: bool) -> Result<u64, BenchError> {
    if should_fail {
        Err(BenchError::Denied)
    } else {
        Ok(checksum_str(0, &s))
    }
}
