namespace Murmur.Core;

/// <summary>One segment returned by the transcription engine.</summary>
/// <param name="Text">The transcribed text, as produced by Whisper.</param>
/// <param name="NoSpeechProbability">
/// Whisper's own confidence that this segment contains no speech. High values are the
/// primary signal that a segment is a silence artifact rather than something the user said.
/// </param>
public sealed record TranscribedSegment(string Text, float NoSpeechProbability);
