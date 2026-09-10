using System;

namespace FrostySdk.Attributes;

public class ArrayNameAttribute : Attribute
{
    public string Name { get; }

    public ArrayNameAttribute(string inName)
    {
        Name = inName;
    }
}