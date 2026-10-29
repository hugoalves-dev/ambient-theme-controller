namespace ThemeProbe;

public sealed class ThemeObserver(ThemeValues initial)
{
    private ThemeValues current = initial;

    public bool Observe(ThemeValues next)
    {
        if (next == current) return false;
        current = next;
        return true;
    }
}
