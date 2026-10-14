
public struct SingleVirbator
{
    public int ActiveCommand;
    public float Duration;
    public int Amplitude;

    public SingleVirbator(int command, float duration, int amplitude)
    {
        duration = duration <= 4 ? 4f : duration;
        amplitude = amplitude < 1 ? 1 : amplitude > 10 ? 10 : amplitude;

        ActiveCommand = command;
        Duration = duration;
        Amplitude = amplitude;
    }
}

public class VibrationData
{
    public SingleVirbator[] Virbators = new SingleVirbator[2];

    public SingleVirbator_20[] Virbator_20 = new SingleVirbator_20[2];

    public VibrationData(SingleVirbator[] virbators)
    {
        if (virbators.Length != 2) return;

        for (int i = 0; i < virbators.Length; i++)
        {
            Virbators[i] = virbators[i];
        }
    }

    public VibrationData(SingleVirbator_20[] Virbator20)
    {
        if (Virbator20.Length != 2) return;

        for (int i = 0; i < Virbator20.Length; i++)
        {
            Virbator_20[i] = Virbator20[i];
        }
    }
}

public struct SingleVirbator_20
{
    public int ActiveCommand;
    public int Duration;
    public int Amplitude;
    public int Rate;

    public SingleVirbator_20(int command, int duration, int amplitude, int rate)
    {
        duration = duration <= 40 ? 40 : duration >= 30000 ? 30000 : duration;
        amplitude = amplitude < 1 ? 1 : amplitude > 1000 ? 1000 : amplitude;
        rate = rate < 7 ? 7 : rate > 3000 ? 3000 : rate;

        ActiveCommand = command;
        Duration = duration;
        Amplitude = amplitude;
        Rate = rate;
    }
}
