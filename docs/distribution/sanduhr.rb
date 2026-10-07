cask "sanduhr" do
  version "2.9.0"
  sha256 "f39c579a3cdf9a8a70dbc5b7d5d254f4279096b6069492fb66cbcfd8eb2ac15c"

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
