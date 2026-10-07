cask "sanduhr" do
  version "2.8.0"
  sha256 "ed69e1a3d0500a4519e9eb48fcb54c276768b5b1852759a9d85becdc893630ba"

  url "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v#{version}-mac/Sanduhr-#{version}.dmg",
      verified: "github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/"
  name "Sanduhr für Claude"
  desc "Native desktop widget tracking Claude.ai subscription usage with burn-rate projections"
  homepage "https://estevanhernandez-stack-ed.github.io/Sanduhr_f-r_Claude/"

  livecheck do
    url :url
    strategy :github_latest
    regex(/^v(\d+(?:\.\d+)+)-mac$/i)
  end

  auto_updates true
  depends_on macos: ">= :sonoma"

  app "Sanduhr.app"

  zap trash: [
    "~/Library/Application Support/Sanduhr",
    "~/Library/Preferences/com.626labs.sanduhr.plist",
    "~/Library/Preferences/com.626labs.sanduhr.desk.plist",
  ]
end
