using System;

namespace FrostySdk.Attributes;


/// <summary>
/// Specifies that this property is read only
/// </summary>
[AttributeUsage(FrostyAttributeTargets.Field)]
public class IsReadOnlyAttribute : Attribute
{
}