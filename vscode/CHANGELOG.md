# Changelog

## [Unreleased]

## [0.2.0]

- **Your time counts the read-and-dictate gap (#175).** A prompt's span now reaches back to the last Claude activity in its own session when that's within your idle minutes (default 15). The time spent reading Claude's answer, thinking and dictating the next prompt is yours. A builder who orchestrates and rarely types got only 2 minutes per prompt before. Longer gaps keep the 2-minute span, and a subagent's activity never shortens the gap. On Neb's Oct 21, your time went from 3.97 h to 5.07 h.
- Day files say which rule made them: `caveats.readerVersion` is `0.2.0`.

## [0.1.0]

- Initial scaffold.
