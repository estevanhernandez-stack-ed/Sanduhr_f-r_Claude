cask "sanduhr" do
  version "2.14.0"
  sha256 "9a7ab340200de9091165f11bd8d6a285f8ddc8fc227ab03a6d5b52a132ab2215"

  url "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v#{version}-mac/Sanduhr-#{version}.dmg"
  name "Sanduhr für Claude"
  desc "Claude.ai subscription usage widget with burn-rate alerts"
  homepage "https://estevanhernandez-stack-ed.github.io/Sanduhr_f-r_Claude/"

  livecheck do
    url :url
    strategy :github_latest
    regex(/^v(\d+(?:\.\d+)+)-mac$/i)
  end

  auto_updates true
  depends_on macos: :sonoma

  app "Sanduhr.app"

  zap trash: [
    "~/Library/Application Support/Sanduhr",
    "~/Library/Caches/com.626labs.sanduhr",
    "~/Library/Caches/Sparkle_com.626labs.sanduhr",
    "~/Library/Preferences/com.626labs.sanduhr.desk.plist",
    "~/Library/Preferences/com.626labs.sanduhr.plist",
  ]
end
