using System;

namespace FrostySdk.Attributes;

public static class FrostyAttributeTargets
{
    public const AttributeTargets Type = AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum |
                                           AttributeTargets.Interface | AttributeTargets.Delegate |
                                           AttributeTargets.Method;

    public const AttributeTargets Field = AttributeTargets.Property | AttributeTargets.Field;
}