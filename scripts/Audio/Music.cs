using Godot;

/// <summary>Музыка. Считается на ходу, а не проигрывается файлом.</summary>
/// <remarks>
/// Своя, потому что чужую в игру не положишь, и генератор вместо записи — потому что час
/// такой музыки весит гигабайт, а повторяющаяся минута надоедает к третьему году партии.
/// Здесь она не повторяется: гармония крутится по кругу, а всё остальное каждый раз
/// раскладывается заново.
/// </remarks>
public partial class Music : AudioStreamPlayer
{
    private const int Rate = 44100;

    /// <summary>Длина такта в секундах. Медленно: это фон под таблицы, а не марш.</summary>
    private const double Bar = 8.0;

    /// <summary>На сколько вперёд раскладываются ноты. Меньше буфера генератора, иначе
    /// нота успеет прозвучать до того, как её запишут.</summary>
    private const double Horizon = 1.0;

    /// <summary>Ход гармонии. Ля минор: доминанты нет, поэтому круг не тянет к развязке и
    /// его можно слушать часами.</summary>
    private static readonly int[][] Chords =
    [
        [57, 60, 64], // Am
        [53, 57, 60], // F
        [48, 55, 60], // C
        [55, 59, 62], // G
    ];

    private static readonly int[] Roots = [45, 41, 36, 43];

    /// <summary>Ноты, из которых берутся переливы поверх аккорда.</summary>
    private static readonly int[] Steps = [0, 3, 5, 7, 10, 12];

    private readonly List<Voice> _voices = [];
    private readonly Random _random = new();

    private AudioStreamGeneratorPlayback _out = null!;
    private Vector2[] _chunk = [];

    private double _time;
    private double _scheduled;
    private int _bar;

    // Эхо: своё, а не эффектом шины — так задержка попадает в долю такта.
    private float[] _echoLeft = [];
    private float[] _echoRight = [];
    private int _echoAt;

    /// <summary>Громкость ступенями: полная, тихая, выключено.</summary>
    private static readonly float[] Levels = [-9f, -19f, -80f];
    private int _level;

    public override void _Ready()
    {
        Bus = Reverb();

        var generator = new AudioStreamGenerator { MixRate = Rate, BufferLength = 0.5f };

        Stream = generator;
        VolumeDb = Levels[_level];
        Play();

        _out = (AudioStreamGeneratorPlayback)GetStreamPlayback();
        _chunk = new Vector2[Rate / 10];

        var echo = (int)(Bar / 16 * Rate);
        _echoLeft = new float[echo];
        _echoRight = new float[echo];
    }

    public override void _Process(double delta)
    {
        Schedule();
        Render();
    }

    /// <summary>Следующая ступень громкости.</summary>
    public void Cycle()
    {
        _level = (_level + 1) % Levels.Length;
        VolumeDb = Levels[_level];
    }

    public bool Loud => _level < Levels.Length - 1;

    /// <summary>Раскладывает ноты такта наперёд.</summary>
    private void Schedule()
    {
        while (_scheduled < _time + Horizon)
        {
            var at = _scheduled;
            var chord = Chords[_bar % Chords.Length];
            var root = Roots[_bar % Roots.Length];

            // Подложка: три голоса аккорда с расстройкой. Расстройка и даёт то самое
            // медленное биение, из-за которого звук кажется живым.
            for (var voice = 0; voice < chord.Length; voice++)
            {
                var pan = -0.5f + voice * 0.5f;

                Add(new Voice(Hz(chord[voice]), at, Bar + 2.5, 0.085f, pan, Kind.Pad));
                Add(new Voice(Hz(chord[voice]) * 1.0015, at, Bar + 2.5, 0.085f, -pan, Kind.Pad));
            }

            Add(new Voice(Hz(root), at, Bar + 1.0, 0.16f, 0f, Kind.Bass));

            // Переливы: два-четыре на такт, всегда по нотам аккорда, поэтому мимо не
            // попадают, как бы ни легли.
            var count = 2 + _random.Next(3);
            for (var note = 0; note < count; note++)
            {
                var when = at + _random.NextDouble() * (Bar - 1.0);
                var step = Steps[_random.Next(Steps.Length)];
                var octave = 12 * (1 + _random.Next(2));

                Add(new Voice(Hz(root + step + octave), when, 3.0,
                    0.05f + (float)_random.NextDouble() * 0.03f,
                    (float)(_random.NextDouble() * 1.4 - 0.7), Kind.Pluck));
            }

            _scheduled += Bar;
            _bar++;
        }
    }

    private void Add(Voice voice) => _voices.Add(voice);

    private void Render()
    {
        var frames = Math.Min(_out.GetFramesAvailable(), _chunk.Length);
        if (frames <= 0) return;

        for (var frame = 0; frame < frames; frame++)
        {
            var now = _time + (double)frame / Rate;
            var left = 0f;
            var right = 0f;

            foreach (var voice in _voices)
            {
                if (now < voice.Start || now > voice.Start + voice.Length) continue;

                var sample = voice.At(now);

                left += sample * (1f - voice.Pan) * 0.5f;
                right += sample * (1f + voice.Pan) * 0.5f;
            }

            // Эхо крест-накрест: левое возвращается справа, и сцена раздвигается.
            var echoLeft = _echoLeft[_echoAt];
            var echoRight = _echoRight[_echoAt];

            _echoLeft[_echoAt] = left + echoRight * 0.34f;
            _echoRight[_echoAt] = right + echoLeft * 0.34f;
            _echoAt = (_echoAt + 1) % _echoLeft.Length;

            left += echoLeft * 0.3f;
            right += echoRight * 0.3f;

            _chunk[frame] = new Vector2(Soft(left), Soft(right));
        }

        _out.PushBuffer(_chunk.AsSpan(0, frames).ToArray());
        _time += (double)frames / Rate;

        _voices.RemoveAll(voice => voice.Start + voice.Length < _time);
    }

    /// <summary>Мягкое ограничение: сумма голосов иногда перехлёстывает единицу, и без
    /// него на пиках слышен треск.</summary>
    private static float Soft(float value) => (float)Math.Tanh(value * 1.4);

    private static double Hz(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

    /// <summary>Отдельная шина с залом: без неё голоса звучат сухо и по-игрушечному.</summary>
    private static string Reverb()
    {
        const string name = "Music";
        if (AudioServer.GetBusIndex(name) >= 0) return name;

        var index = AudioServer.BusCount;

        AudioServer.AddBus(index);
        AudioServer.SetBusName(index, name);
        AudioServer.AddBusEffect(index, new AudioEffectReverb
        {
            RoomSize = 0.86f,
            Damping = 0.45f,
            Spread = 1f,
            Wet = 0.34f,
            Dry = 0.82f,
        });

        return name;
    }

    private enum Kind
    {
        Pad,
        Bass,
        Pluck,
    }

    /// <summary>Один звучащий голос: частота, когда начался, сколько живёт и чем звучит.</summary>
    private readonly record struct Voice(double Freq, double Start, double Length, float Amp, float Pan, Kind Kind)
    {
        public float At(double now)
        {
            var age = now - Start;
            var phase = Math.Tau * Freq * age;

            var (tone, envelope) = Kind switch
            {
                Kind.Pad => (
                    Math.Sin(phase) + 0.42 * Math.Sin(phase * 2) + 0.16 * Math.Sin(phase * 3),
                    Fade(age, Length, 2.6, 2.6)),

                Kind.Bass => (
                    Math.Sin(phase) + 0.18 * Math.Sin(phase * 2),
                    Fade(age, Length, 0.9, 2.0)),

                _ => (
                    Math.Sin(phase) + 0.35 * Math.Sin(phase * 2) + 0.12 * Math.Sin(phase * 4),
                    Math.Exp(-age * 2.4) * Math.Min(age * 120, 1)),
            };

            return (float)(tone * envelope * Amp);
        }

        /// <summary>Плавный вход и выход. Резкий край даёт щелчок.</summary>
        private static double Fade(double age, double length, double rise, double fall) =>
            Math.Clamp(Math.Min(age / rise, (length - age) / fall), 0, 1);
    }
}
