using System;

namespace FrostySdk.Attributes;


[AttributeUsage(AttributeTargets.Assembly)]
public class SdkVersionAttribute : Attribute
{
    public uint Head { get; }

    public SdkVersionAttribute(uint inHead)
    {
        Head = inHead;
    }
}