cask "sanduhr" do
  version "2.5.0"
  sha256 "d1b345f5d0fd37b1c6530dbdb446de14315f7f7ceddc344e214e4219543f3be5"

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
