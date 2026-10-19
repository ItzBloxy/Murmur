# Audio fixtures

Four tests skip themselves until these files exist:

- `WhisperEngineTests.Transcribes_danish_within_the_word_error_tolerance`
- `WhisperEngineTests.Transcribes_english_within_the_word_error_tolerance`
- `SileroVoiceSegmenterTests.Speech_followed_by_silence_emits_one_segment`
- `SileroVoiceSegmenterTests.ContainsSpeech_is_true_for_real_speech`

## What to record

Two clips, spoken into the microphone you will actually dictate with — the point is to
catch problems with *your* setup, not with a clean studio recording. Five to eight seconds
each. Speak normally, at the pace and distance you would really use.

Suggested sentences, chosen because they mix everyday words with the proper nouns and
technical vocabulary Whisper most often gets wrong:

| File | Say |
|---|---|
| `danish.wav` | Jeg sender rapporten til Anna inden fredag klokken tolv. |
| `english.wav` | Deploy the staging branch and check the Kubernetes logs afterwards. |

Optionally, a third for Whistle, which unskips
`WhistleEngineTests.Transcribes_german_within_the_word_error_tolerance`:

| File | Say |
|---|---|
| `german.wav` | Bitte schick den Bericht bis Freitag an das Finanzteam in München. |

Then write **exactly what you actually said** into the matching `.txt` file — `danish.txt`
and `english.txt`. That text is the word-error-rate reference. If you improvise different
words, the `.txt` must match the improvisation, not the table above.

## Required format

16 kHz, mono, 16-bit PCM WAV. Anything else will be misread.

Record however you like, then convert:

```bash
ffmpeg -i recording.wav -ar 16000 -ac 1 -c:a pcm_s16le tests/Murmur.Integration.Tests/fixtures/danish.wav
```

Audacity works too: set the project rate to 16000 Hz, Tracks → Mix → Mix Stereo down to
Mono, then File → Export → WAV (Microsoft) signed 16-bit PCM.

## Verifying

```bash
dotnet test --project tests/Murmur.Integration.Tests/Murmur.Integration.Tests.csproj
```

All four should switch from skipped to passing. If a word-error-rate assertion fails, check
the `.txt` against what you actually said before suspecting the model — a mismatched
reference is the usual cause.
