using System;

namespace FrostySdk.Attributes;

/// <summary>
/// Specifies that this property should not be saved
/// </summary>
[AttributeUsage(FrostyAttributeTargets.Field)]
public class IsTransientAttribute : Attribute
{
}