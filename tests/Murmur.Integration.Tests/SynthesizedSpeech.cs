namespace Murmur.Integration.Tests;

/// <summary>
/// Speech from the installed Windows voice, for tests that need words rather than silence.
///
/// Drives SAPI over COM rather than through the System.Speech package: System.Speech
/// matches voices by CultureInfo, and under this repository's InvariantGlobalization every
/// match fails with a NullReferenceException deep inside it. SAPI chooses voices natively.
/// </summary>
public static class SynthesizedSpeech
{
    /// <summary>SpeechAudioFormatType.SAFT16kHz16BitMono.</summary>
    private const int Sapi16kHz16BitMono = 18;

    public static float[] Render(string text)
    {
        dynamic voice = CreateCom("SAPI.SpVoice");
        dynamic format = CreateCom("SAPI.SpAudioFormat");
        dynamic stream = CreateCom("SAPI.SpMemoryStream");

        // 409 is en-US. Every Windows install has one, and Whistle speaks English.
        dynamic english = voice.GetVoices("Language=409", "");
        if (english.Count == 0) throw new InvalidOperationException("No en-US Windows voice is installed.");
        voice.Voice = english.Item(0);

        format.Type = Sapi16kHz16BitMono;
        stream.Format = format;
        voice.AudioOutputStream = stream;
        voice.Speak(text);

        byte[] bytes = stream.GetData();
        var samples = new float[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
        }

        return samples;
    }

    private static object CreateCom(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)!;
}
