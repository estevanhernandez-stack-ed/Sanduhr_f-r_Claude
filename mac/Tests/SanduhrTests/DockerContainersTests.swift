import Foundation
import Testing
@testable import Sanduhr

/// The container list (spec section 15): only the fields the list call returns, runners picked by
/// image name, and the status line under the switch. Ported from Windows' DockerContainersTests.
@Suite("Docker containers")
struct DockerContainersTests {
    static let id1 = String(repeating: "1", count: 64)
    static let id2 = String(repeating: "2", count: 64)
    static let id3 = String(repeating: "3", count: 64)

    // The shape of GET /containers/json, trimmed to the fields Sanduhr reads.
    static let json = """
        [
          {"Id":"\(id1)","Names":["/ci-runners-alpha-1-1"],"Image":"ci-host-gh-runner:2.337.0","State":"running",
           "Labels":{"com.docker.compose.project":"ci-runners","com.docker.compose.service":"alpha-1"}},
          {"Id":"\(id2)","Names":["/ci-runners-egress-proxy-1"],"Image":"ci-host-egress-proxy:1","State":"running",
           "Labels":{"com.docker.compose.service":"egress-proxy"}},
          {"Id":"\(id3)","Names":["/my-runner"],"Image":"ghcr.io/actions/actions-runner:latest","State":"exited","Labels":{}}
        ]
        """

    private func parse(_ s: String) -> [DockerContainer] { DockerContainers.parse(Data(s.utf8)) }

    @Test func parsesTheListCall() {
        #expect(parse(Self.json) == [
            DockerContainer(id: Self.id1, image: "ci-host-gh-runner:2.337.0", name: "alpha-1", running: true,
                            key: "ci-runners-alpha-1-1"),
            DockerContainer(id: Self.id2, image: "ci-host-egress-proxy:1", name: "egress-proxy", running: true,
                            key: "ci-runners-egress-proxy-1"),
            DockerContainer(id: Self.id3, image: "ghcr.io/actions/actions-runner:latest", name: "my-runner",
                            running: false, key: "my-runner"),
        ])
    }

    @Test func anIdWithATrailingNewlineIsSkipped() {
        #expect(parse(#"[{"Id":"\#(Self.id1)\n","Image":"ci-host-gh-runner:1","State":"running","Labels":{}}]"#).isEmpty)
    }

    @Test func runnersAreRunningContainersWithARunnerImage() {
        #expect(DockerContainers.runners(parse(Self.json)).map(\.name) == ["alpha-1"])
    }

    @Test func replicasThatShareALabelShowTheirContainerNames() {
        let json = """
            [
              {"Id":"\(Self.id1)","Names":["/ci-runner-1"],"Image":"myoung34/github-runner:latest","State":"running","Labels":{"com.docker.compose.service":"runner"}},
              {"Id":"\(Self.id2)","Names":["/ci-runner-2"],"Image":"myoung34/github-runner:latest","State":"running","Labels":{"com.docker.compose.service":"runner"}},
              {"Id":"\(Self.id3)","Names":["/ci-other-1"],"Image":"myoung34/github-runner:latest","State":"running","Labels":{"com.docker.compose.service":"other"}}
            ]
            """
        let runners = DockerContainers.runners(parse(json))
        #expect(runners.map(\.name) == ["ci-runner-1", "ci-runner-2", "other"])
        #expect(runners.map(\.key) == ["ci-runner-1", "ci-runner-2", "ci-other-1"])
    }

    @Test func theKeyIsTheFirstContainerNameOrTheId() {
        let list = parse(#"[{"Id":"\#(Self.id1)","Image":"gh-runner","State":"running","Labels":{}}]"#)
        #expect(list[0].key == Self.id1)
    }

    @Test(arguments: [
        ("ci-host-gh-runner:2.337.0", true),
        ("ci-host-gh-runner:2.338.0", true),
        ("ghcr.io/actions/actions-runner:2.321.0", true),
        ("myoung34/github-runner:latest", true),
        ("summerwind/actions-runner@sha256:0123abcd", true),
        ("localhost:5000/team/GH-Runner", true),
        ("ci-host-egress-proxy:1", false),
        ("postgres:16", false),
        ("registry.gh-runner.example.com/postgres:16", false), // only the image's own name counts
        ("", false),
    ])
    func runnerImages(image: String, expected: Bool) {
        #expect(DockerContainers.isRunnerImage(image) == expected)
    }

    @Test(arguments: [
        "not json", "{}", #"[{"Id":"short","Image":"ci-host-gh-runner:1","State":"running"}]"#,
    ])
    func malformedIsEmpty(json: String) {
        #expect(parse(json).isEmpty)
    }

    @Test func aNameIsOneShortLine() {
        let long = String(repeating: "n", count: 80)
        let list = parse(#"[{"Id":"\#(Self.id1)","Names":["/x"],"Image":"gh-runner","State":"running","Labels":{"com.docker.compose.service":"\#(long)"}}]"#)
        #expect(list[0].name.count <= 40)
    }

    @Test func statusLines() {
        #expect(RunnerStatus.off.text == "")
        #expect(RunnerStatus(kind: .watching, runners: ["alpha-1"]).text == "Watching 1 runner: alpha-1")
        #expect(RunnerStatus(kind: .watching, runners: ["echo-1", "echo-2"]).text == "Watching 2 runners: echo-1, echo-2")
        #expect(RunnerStatus(kind: .noRunners).text == "No runner containers are running.")
        #expect(RunnerStatus(kind: .dockerDown).text == "Docker isn't running.")
        #expect(RunnerStatus(kind: .dockerRefused).text == "Docker answered but refused the request.")
        #expect(RunnerStatus(kind: .dockerDenied).text == "Docker refused the connection. Check that your account can use Docker's socket.")
    }

    @Test func aLongRunnerListIsClipped() {
        let names = (1...30).map { "runner-number-\($0)" }
        let text = RunnerStatus(kind: .watching, runners: names).text
        #expect(text.hasPrefix("Watching 30 runners: runner-number-1, "))
        #expect(text.count <= RunnerStatus.maxText)
        #expect(text.hasSuffix("…"))
    }

    @Test func idRuleIsExactlySixtyFourLowercaseHex() {
        #expect(DockerContainers.isID(Self.id1))
        #expect(!DockerContainers.isID(String(repeating: "A", count: 64)))
        #expect(!DockerContainers.isID(String(repeating: "1", count: 63)))
    }
}
