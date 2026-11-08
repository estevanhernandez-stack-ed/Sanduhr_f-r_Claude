cask "sanduhr" do
  version "2.42.0"
  sha256 "2d7991309b6152311707d0a741e6dbee238fa1a46ae080146f9c56046972eea7"

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
