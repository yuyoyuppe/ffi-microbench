fn main() {
    println!("cargo:rerun-if-changed=src/api_csb.rs");
    csbindgen::Builder::default()
        .input_extern_file("src/api_csb.rs")
        .csharp_dll_name("benchffi")
        .csharp_class_name("CsbNative")
        .csharp_namespace("CsBindgen")
        .csharp_class_accessibility("internal")
        .generate_csharp_file("../dotnet/FfiBench/Generated/NativeMethods.g.cs")
        .unwrap();
}
