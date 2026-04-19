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
        .executableTarget(
            name: "Sanduhr",
            dependencies: [
                .product(name: "Sparkle", package: "Sparkle"),
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
            path: "Tests/SanduhrTests"
        ),
    ]
)
