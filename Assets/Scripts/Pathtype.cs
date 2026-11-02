using System;

public enum PathType { Straight, Corner, Cross }

// Which sides of a tile the path connects to. Flags so we can combine them.
[Flags]
public enum Openings
{
    None = 0,
    North = 1, // +Z
    East = 2,  // +X
    South = 4, // -Z
    West = 8   // -X
}

public static class PathUtil
{
    // Openings of each path type at rotation 0.
    public static Openings BaseOpenings(PathType type)
    {
        switch (type)
        {
            case PathType.Straight: return Openings.North | Openings.South;
            case PathType.Corner:   return Openings.North | Openings.East;
            case PathType.Cross:    return Openings.North | Openings.East | Openings.South | Openings.West;
            default:                return Openings.None;
        }
    }

    // Rotate openings clockwise (seen from above) by 90 degrees * steps.
    public static Openings Rotate(Openings o, int steps)
    {
        steps = ((steps % 4) + 4) % 4;
        int v = (int)o;
        for (int i = 0; i < steps; i++)
            v = ((v << 1) & 0xF) | ((v >> 3) & 1);
        return (Openings)v;
    }
}