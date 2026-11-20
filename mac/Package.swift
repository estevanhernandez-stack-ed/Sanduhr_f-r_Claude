// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Sanduhr",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "Sanduhr", targets: ["Sanduhr"]),
    ],
    dependencies: [
        .package(url: "https://github.com/sparkle-project/Sparkle", from: "2.6.0"),
    ],
    targets: [
        // zstd v1.5.7's decoder-only single file (see the header comment in zstddeclib.c). Codex
        // archives its older rollouts as .jsonl.zst; macOS has no zstd and Apple's Compression does
        // not decode it. -w: the vendored file is upstream's, not ours to fix, and a warning from
        // it must not turn up in our build output.
        .target(
            name: "CZstd",
            path: "Sources/CZstd",
            cSettings: [
                .unsafeFlags(["-w"]),
            ]
        ),
        .executableTarget(
            name: "Sanduhr",
            dependencies: [
                .product(name: "Sparkle", package: "Sparkle"),
                "CZstd",
            ],
            path: "Sources/Sanduhr",
            linkerSettings: [
                // Sparkle.framework ships in Contents/Frameworks/; the main
                // executable lives in Contents/MacOS/, so @executable_path/..
                // points at Contents/ and ../Frameworks resolves correctly.
                .unsafeFlags(["-Xlinker", "-rpath",
                              "-Xlinker", "@executable_path/../Frameworks"]),
            ]
        ),
        .testTarget(
            name: "SanduhrTests",
            dependencies: ["Sanduhr"],
            path: "Tests/SanduhrTests",
            // Synthetic Claude Code session logs (item 45), ported from the Windows tests' inputs.
            resources: [.copy("Fixtures")]
        ),
    ]
)
