// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Sanduhr",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "Sanduhr", targets: ["Sanduhr"]),
    ],
    targets: [
        .executableTarget(
            name: "Sanduhr",
            path: "Sources/Sanduhr"
        ),
    ]
)
