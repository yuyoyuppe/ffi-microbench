//! Single test cdylib exposing the same operations through three FFI surfaces so
//! compiler settings are identical (see uniffi_migration_benchmarks.md):
//!   - `api_csb`:    hand-rolled `extern "C"` exports mirroring the reference app's native-dll
//!                   conventions, consumed via csbindgen-generated P/Invoke.
//!   - `api_uniffi`: uniffi 0.31 proc-macro exports, consumed via uniffi-bindgen-cs.
//!   - `api_raw`:    hand-written `*_raw` exports backing the fork's
//!                   `high_performance_strings` span fast path.

uniffi::setup_scaffolding!();

pub mod api_csb;
pub mod api_raw;
pub mod api_uniffi;
pub mod types;
