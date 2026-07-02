//! Shared payload types and lazily-built static test data. Data is built once and
//! leaked so give_* calls measure only the FFI-mandated copy, not payload generation.

use std::collections::HashMap;
use std::sync::{Mutex, OnceLock};

/// ~6 mixed fields, mirrors `AppWindowRequest`-like payloads (case 8).
#[derive(Clone, Debug, serde::Serialize, serde::Deserialize, uniffi::Record)]
pub struct BenchRecord {
    pub id: u32,
    pub title: String,
    pub app_path: String,
    pub enabled: bool,
    pub score: u32,
    pub tag: Option<String>,
}

/// Mirrors `PwaInfo` (case 9).
#[derive(Clone, Debug, serde::Serialize, serde::Deserialize, uniffi::Record)]
pub struct PwaInfo {
    pub app_id: String,
    pub start_url: Option<String>,
    pub browser_path: Option<String>,
}

/// Branch-heavy nested/optional record mirroring `AppWindowRequest` + `PwaInfo` (case 9).
#[derive(Clone, Debug, serde::Serialize, serde::Deserialize, uniffi::Record)]
pub struct WindowRequest {
    pub title: Option<String>,
    pub class_name: Option<String>,
    pub exe_path: Option<String>,
    pub window_ids: Vec<u32>,
    pub pwa: Option<PwaInfo>,
    pub timeout_ms: u32,
    pub focus: bool,
}

fn ascii_payload(n: usize) -> String {
    const PATTERN: &str = "ffi-microbench-payload-0123456789-";
    let mut s = String::with_capacity(n + PATTERN.len());
    while s.len() < n {
        s.push_str(PATTERN);
    }
    s.truncate(n);
    s
}

pub fn static_string(n: usize) -> &'static str {
    static CACHE: OnceLock<Mutex<HashMap<usize, &'static str>>> = OnceLock::new();
    let mut cache = CACHE.get_or_init(Default::default).lock().unwrap();
    cache
        .entry(n)
        .or_insert_with(|| Box::leak(ascii_payload(n).into_boxed_str()))
}

pub fn static_bytes(n: usize) -> &'static [u8] {
    static CACHE: OnceLock<Mutex<HashMap<usize, &'static [u8]>>> = OnceLock::new();
    let mut cache = CACHE.get_or_init(Default::default).lock().unwrap();
    cache.entry(n).or_insert_with(|| {
        let mut v = Vec::with_capacity(n);
        v.extend((0..n).map(|i| (i % 251) as u8));
        Box::leak(v.into_boxed_slice())
    })
}

/// `count` elements of ~16 B each (case 7).
pub fn static_string_list(count: usize) -> &'static Vec<String> {
    static CACHE: OnceLock<Mutex<HashMap<usize, &'static Vec<String>>>> = OnceLock::new();
    let mut cache = CACHE.get_or_init(Default::default).lock().unwrap();
    cache.entry(count).or_insert_with(|| {
        Box::leak(Box::new(
            (0..count).map(|i| format!("hotkey-item-{i:05}")).collect(),
        ))
    })
}

pub fn make_record(i: u32) -> BenchRecord {
    BenchRecord {
        id: i,
        title: format!("Window Title {i}"),
        app_path: format!("C:\\Program Files\\App{}\\app.exe", i % 17),
        enabled: i % 2 == 0,
        score: i.wrapping_mul(2654435761),
        tag: if i % 3 == 0 {
            Some(format!("tag-{}", i % 7))
        } else {
            None
        },
    }
}

pub fn static_records(count: usize) -> &'static Vec<BenchRecord> {
    static CACHE: OnceLock<Mutex<HashMap<usize, &'static Vec<BenchRecord>>>> = OnceLock::new();
    let mut cache = CACHE.get_or_init(Default::default).lock().unwrap();
    cache.entry(count).or_insert_with(|| {
        Box::leak(Box::new((0..count as u32).map(make_record).collect()))
    })
}

pub fn make_request() -> WindowRequest {
    WindowRequest {
        title: Some("Example Settings — Extensions".into()),
        class_name: None,
        exe_path: Some("C:\\Users\\user\\AppData\\Local\\Programs\\ExampleApp\\ExampleApp.exe".into()),
        window_ids: vec![0x1a2b3c, 0x2b3c4d, 0x3c4d5e, 0x4d5e6f],
        pwa: Some(PwaInfo {
            app_id: "com.example.pwa.settings".into(),
            start_url: Some("https://app.example.com/settings?tab=extensions".into()),
            browser_path: None,
        }),
        timeout_ms: 1500,
        focus: true,
    }
}

pub fn static_request() -> &'static WindowRequest {
    static REQ: OnceLock<WindowRequest> = OnceLock::new();
    REQ.get_or_init(make_request)
}

pub fn static_map(count: usize) -> &'static HashMap<String, String> {
    static CACHE: OnceLock<Mutex<HashMap<usize, &'static HashMap<String, String>>>> =
        OnceLock::new();
    let mut cache = CACHE.get_or_init(Default::default).lock().unwrap();
    cache.entry(count).or_insert_with(|| {
        Box::leak(Box::new(
            (0..count)
                .map(|i| (format!("setting-key-{i:05}"), format!("setting-value-{i:05}")))
                .collect(),
        ))
    })
}

/// Cheap fold so returned checksums depend on every element and can't be elided.
pub fn checksum_str(acc: u64, s: &str) -> u64 {
    acc.wrapping_mul(31).wrapping_add(s.len() as u64)
}
