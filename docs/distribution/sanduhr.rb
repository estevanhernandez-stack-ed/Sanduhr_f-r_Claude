cask "sanduhr" do
  version "2.10.0"
  sha256 "f4a2311e6d5d376117514162e54211c383918fe934eea7bb8a2fd506fd1427ce"

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
