namespace Soqet3.Structs;

public struct Channel : IEquatable<Channel>
{
    public string Name { get; set; }
    public string Address { get; set; }

    public bool Equals(Channel other)
    {
        return Name == other.Name && Address == other.Address;
    }

    public override bool Equals(object? obj)
    {
        return obj is Channel other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Address);
    }

    public static bool operator ==(Channel left, Channel right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Channel left, Channel right)
    {
        return !(left == right);
    }
}
