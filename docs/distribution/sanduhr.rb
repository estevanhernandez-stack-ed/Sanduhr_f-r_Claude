cask "sanduhr" do
  version "2.43.1"
  sha256 "552443c3848b9a172300f4577c672d1db93211ebac3fc92caaf1d3af22651c86"

  url "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases/download/v#{version}-mac/Sanduhr-#{version}.dmg"
  name "Sanduhr für Claude"
  desc "Claude.ai usage meters on the desktop, with Claude Code mods and config packs"
  homepage "https://626labs.dev/sanduhr/"

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
