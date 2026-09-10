using Frosty.Hash;
using FrostySdk.Attributes;
using FrostySdk.IO;
using FrostySdk.Managers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using FrostySdk.Ebx;
using FrostySdk.Managers.Entries;

namespace FrostySdk.Attributes
{
    #region -- Attributes --

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class ResCustomHandlerAttribute : Attribute
    {
        public ResourceType ResType { get; set; }
        public Type CustomHandler { get; set; }
        public ResCustomHandlerAttribute(ResourceType resType, Type customHandler)
        {
            ResType = resType;
            CustomHandler = customHandler;
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
    public class HashAttribute : Attribute
    {
        public int Hash { get; set; }
        public HashAttribute(int inHash) { Hash = inHash; }
    }

    /// <summary>
    /// Used by Anthem to obtain the correct class during ebx reading
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Delegate, AllowMultiple = true, Inherited = false)]
    public class TypeInfoGuidAttribute : Attribute
    {
        public Guid Guid { get; set; }
        public TypeInfoGuidAttribute(string inGuid) { Guid = Guid.Parse(inGuid); }
    }

    /// <summary>
    /// Specifies the signature of the type
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Delegate, AllowMultiple = true, Inherited = false)]
    public class TypeInfoSignatureAttribute : Attribute
    {
        public uint Signature { get; set; }
        public TypeInfoSignatureAttribute(int inSignature) { Signature = (uint)inSignature; }
    }

    /// <summary>
    /// Specifies that the class requires a converter to convert to/from some custom type
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ClassConverterAttribute : Attribute
    {
        public Type Type { get; set; }
        public ClassConverterAttribute(string inType) { Type = null; }
        public ClassConverterAttribute(Type inType) { Type = inType; }
    }

    /// <summary>
    /// Specifies that the class can have instances created even if it subclasses from a class that cannot
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class IsInlineAttribute : Attribute
    {
        public IsInlineAttribute()
        {
        }
    }

    /// <summary>
    /// Specifies that the class can not be created directly
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class IsAbstractAttribute : Attribute
    {
        public IsAbstractAttribute()
        {
        }
    }

    /// <summary>
    /// Specifies the icon this class should use
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class IconAttribute : Attribute
    {
        public string Icon { get; set; }
        public IconAttribute(string inIcon)
        {
            Icon = inIcon;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum)]
    public class RuntimeSizeAttribute : Attribute
    {
        public int Size { get; set; }
        public RuntimeSizeAttribute(int inSize)
        {
            Size = inSize;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ForceAlignAttribute : Attribute
    {
        public ForceAlignAttribute()
        {
        }
    }

    /// <summary>
    /// Sepcifies that this property is dependent on the specified property. The specified property must be a bool
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class DependsOnAttribute : Attribute
    {
        public string Name { get; set; }
        public DependsOnAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Specifies that this property should not show its array items
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class HideChildrentAttribute : Attribute
    {
        public HideChildrentAttribute()
        {
        }
    }

    /// <summary>
    /// Sets the description to display for the property/class in the Property Grid
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class DescriptionAttribute : Attribute
    {
        public string Description { get; set; }
        public DescriptionAttribute(string desc) { Description = desc; }
    }

    /// <summary>
    /// Sets the type of property grid editor to use for the property
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class EditorAttribute : Attribute
    {
        public Type EditorType { get; set; }
        public EditorAttribute(Type type)
        {
            EditorType = type;
        }
    }

    /// <summary>
    /// Specifies that this property is only a reference (does not increment ref-count)
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class IsReferenceAttribute : Attribute
    {
        public IsReferenceAttribute()
        {
        }
    }

    /// <summary>
    /// Specifies that this property (array/struct) is expanded when first loaded
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class IsExpandedByDefaultAttribute : Attribute
    {
        public IsExpandedByDefaultAttribute()
        {
        }
    }

    /// <summary>
    /// Specifies that the property is a fixed size array
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class FixedSizeArrayAttribute : Attribute
    {
    }

    /// <summary>
    /// Sets the default value for new instances of the property
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class DefaultValueAttribute : Attribute
    {
        public object DefaultValue { get; set; }
        public DefaultValueAttribute(object value)
        {
            DefaultValue = value;
        }
    }

    /// <summary>
    /// Specifies that this property can be used as a property connection
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class IsPropertyAttribute : Attribute
    {
        public IsPropertyAttribute()
        {
        }
    }

    /// <summary>
    /// Specifies optional meta data specific to an editor
    /// </summary>
    public abstract class EditorMetaDataAttribute : Attribute
    {
        public EditorMetaDataAttribute()
        {
        }
    }

    /// <summary>
    /// 
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class SliderMinMaxAttribute : EditorMetaDataAttribute
    {
        public float MinValue { get; set; }
        public float MaxValue { get; set; }
        public float SmallChange { get; set; }
        public float LargeChange { get; set; }
        public bool IsSnapToTickEnabled { get; set; }

        public SliderMinMaxAttribute(float min, float max, float small, float large, bool snap)
        {
            MinValue = min;
            MaxValue = max;
            SmallChange = small;
            LargeChange = large;
            IsSnapToTickEnabled = snap;
        }
    }

    /// <summary>
    /// Mandatory attribute for all Ebx based classes
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate, AllowMultiple = false)]
    public class EbxClassMetaAttribute : Attribute
    {
        public EbxFieldType Type => (EbxFieldType)((Flags >> 4) & 0x1F);

        public ushort Flags { get; set; }
        public byte Alignment { get; set; }
        public ushort Size { get; set; }
        public string Namespace { get; set; }

        public EbxClassMetaAttribute(ushort flags, byte alignment, ushort size, string nameSpace)
        {
            Flags = flags;
            Alignment = alignment;
            Size = size;
            Namespace = nameSpace;
        }

        public EbxClassMetaAttribute(EbxFieldType type)
        {
            Flags = (ushort)((int)type << 4);
        }
    }
    #endregion
}

namespace FrostySdk
{
    public class BaseTypeOverride
    {
        /// <summary>
        /// Used to load the data from Original into custom fields
        /// </summary>
        public virtual void Load()
        {
        }

        /// <summary>
        /// Used to save custom field values back into Original
        /// </summary>
        public virtual void Save(object e)
        {
        }

        /// <summary>
        /// The original object that this type is used to override and add to
        /// </summary>
        public object Original;
    }

    public abstract class BaseFieldOverride
    {
    }

    internal struct MetaDataType
    {
        public string DisplayName => meta.GetValue<string>("displayName", "");
        public string Description => meta.GetValue<string>("description", "");
        public string Category => meta.GetValue<string>("category", "");
        public string Editor => meta.GetValue<string>("editor", "");
        public string ValueConverter => meta.GetValue<string>("valueConverter", "");

        public bool IsAbstract => meta.GetValue<bool>("abstract", false);
        public bool IsTransient => meta.GetValue<bool>("transient", false);
        public bool IsReadOnly => meta.GetValue<bool>("readOnly", false);
        public bool IsReference => meta.GetValue<bool>("reference", false);
        public bool IsInline => meta.GetValue<bool>("inline", false);
        public bool IsProperty => false;

        private DbObject meta;

        public MetaDataType(DbObject inMeta)
        {
            meta = inMeta;
        }
    }
    internal struct FieldType
    {
        public string Name => name;
        public Type Type => type;
        public Type BaseType => baseType;
        public EbxField? FieldInfo => fieldType;
        public EbxField? ArrayInfo => arrayType;
        public MetaDataType? MetaData => metaData;

        private string name;
        private Type type;
        private Type baseType;
        private EbxField? fieldType;
        private EbxField? arrayType;
        private MetaDataType? metaData;

        internal FieldType(string inName, Type inType, Type inBaseType, EbxField? inFieldType, EbxField? inArrayType = null, MetaDataType? inMetaData = null)
        {
            name = inName;
            type = inType;
            baseType = inBaseType;
            fieldType = inFieldType;
            arrayType = inArrayType;
            metaData = inMetaData;
        }
    }

    public static class TypeLibrary
{
    public static bool IsInitialized { get; private set; }

    private static readonly Dictionary<string, int> s_nameMapping = new();
    private static readonly Dictionary<uint, int> s_nameHashMapping = new();
    private static readonly Dictionary<Guid, int> s_guidMapping = new();
    private static readonly List<SdkType> s_types = [];
    private static readonly List<TypeInfoAsset> s_typeInfoAssets = [];

    public static bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        FileInfo fileInfo = new(ProfilesLibrary.SdkPath);
        if (!fileInfo.Exists)
        {
            return false;
        }

        Assembly sdk = Assembly.LoadFrom(fileInfo.FullName);

        if ((sdk.GetCustomAttribute<SdkVersionAttribute>()?.Head ?? 0) != FileSystemManager.Head)
        {
            FrostyLogger.Logger?.LogInfo("Outdated Type Sdk, please regenerate it to avoid issues");
        }

        Type[] types = sdk.GetExportedTypes();

        foreach (Type type in types)
        {
            if (type.GetCustomAttribute<EbxTypeMetaAttribute>() is null)
            {
                // should only happen for types that only contain another type
                continue;
            }

            var sdkType = new SdkType(type);

            s_nameMapping.Add(sdkType.Name, s_types.Count);

            uint nameHash = sdkType.NameHash;
            if (nameHash != uint.MaxValue)
            {
                s_nameHashMapping.Add(nameHash, s_types.Count);
            }

            Guid guid = sdkType.Guid;
            if (guid != Guid.Empty)
            {
                s_guidMapping.Add(guid, s_types.Count);
            }

            s_types.Add(sdkType);

            bool addArray = false;
            string? arrayName = type.GetCustomAttribute<ArrayNameAttribute>()?.Name;
            if (arrayName is not null)
            {
                s_nameMapping.Add(arrayName, s_types.Count);
                addArray = true;
            }

            Guid? arrayGuid = type.GetCustomAttribute<ArrayGuidAttribute>()?.Guid;
            if (arrayGuid.HasValue)
            {
                s_guidMapping.Add(arrayGuid.Value, s_types.Count);
                addArray = true;
            }

            if (addArray)
            {
                s_types.Add(new SdkType(typeof(ObservableCollection<>).MakeGenericType(type)));
            }
        }

        IsInitialized = true;
        return true;
    }

    public static void AddTypeInfoAsset(Guid inGuid, object inTypeInfoAsset)
    {
        TypeInfoAsset type = new(inGuid, inTypeInfoAsset);

        const int flag = 1 << 31;
        int index = s_typeInfoAssets.Count | flag;

        if (!string.IsNullOrEmpty(type.Name))
        {
            s_nameMapping.Add(type.Name, index);
        }

        s_guidMapping.Add(type.Guid, index);

        if (type.NameHash != uint.MaxValue)
        {
            s_nameHashMapping.Add(type.NameHash, index);
        }

        s_typeInfoAssets.Add(type);
    }

    public static IEnumerable<IType> EnumerateTypes() => s_types;

    public static IType? GetType(string name)
    {
        if (!s_nameMapping.TryGetValue(name, out int index))
        {
            return null;
        }

        const int flag = 1 << 31;

        return (index & flag) != 0 ? s_typeInfoAssets[index & ~flag] : s_types[index];
    }

    public static IType? GetType(uint nameHash)
    {
        if (!s_nameHashMapping.TryGetValue(nameHash, out int index))
        {
            return null;
        }

        const int flag = 1 << 31;

        return (index & flag) != 0 ? s_typeInfoAssets[index & ~flag] : s_types[index];
    }

    public static IType? GetType(Guid guid)
    {
        if (!s_guidMapping.TryGetValue(guid, out int index))
        {
            return null;
        }

        const int flag = 1 << 31;

        return (index & flag) != 0 ? s_typeInfoAssets[index & ~flag] : s_types[index];
    }

    public static object? CreateObject(string name)
    {
        IType? type = GetType(name);
        return type is null ? null : Activator.CreateInstance(type.Type);
    }

    public static object? CreateObject(uint nameHash)
    {
        IType? type = GetType(nameHash);
        return type is null ? null : Activator.CreateInstance(type.Type);
    }

    public static object? CreateObject(Guid guid)
    {
        IType? type = GetType(guid);
        return type is null ? null : Activator.CreateInstance(type.Type);
    }

    public static bool IsSubClassOf(IType type, string inB)
    {
        IType? checkType = GetType(inB);
        if (checkType is null)
        {
            return false;
        }

        return type.IsSubClassOf(checkType) || checkType.Name == type.Name;
    }

    /// <summary>
    /// Determines if type a derives from type b
    /// </summary>
    /// <param name="inA"></param>
    /// <param name="inB"></param>
    /// <returns></returns>
    public static bool IsSubClassOf(string inA, string inB)
    {
        IType? sourceType = GetType(inA);
        IType? type = GetType(inB);

        return sourceType is not null && type is not null &&
               (sourceType.IsSubClassOf(type) || sourceType.Name == type.Name);
    }

    public static string GetName(this MemberInfo type)
    {
        if (s_nameMapping.TryGetValue(type.Name, out int typeIndex))
        {
            return s_types[typeIndex].Name;
        }

        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")!.Type : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<ArrayNameAttribute>()?.Name ?? type.Name + "-Array";
        }
        return type.GetCustomAttribute<DisplayNameAttribute>()?.Name ?? type.Name;
    }

    public static Guid GetGuid(this MemberInfo type)
    {
        if (s_nameMapping.TryGetValue(type.Name, out int typeIndex))
        {
            return s_types[typeIndex].Guid;
        }

        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")!.Type : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<ArrayGuidAttribute>()?.Guid ?? Guid.Empty;
        }
        return type.GetCustomAttribute<GuidAttribute>()?.Guid ?? Guid.Empty;
    }

    public static uint GetSignature(this MemberInfo type)
    {
        if (s_nameMapping.TryGetValue(type.Name, out int typeIndex))
        {
            return s_types[typeIndex].Signature;
        }

        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")!.Type : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<SignatureAttribute>()?.Signature ?? uint.MaxValue;
        }
        return type.GetCustomAttribute<SignatureAttribute>()?.Signature ?? uint.MaxValue;
    }

    public static uint GetNameHash(this MemberInfo type)
    {
        if (s_nameMapping.TryGetValue(type.Name, out int typeIndex))
        {
            return s_types[typeIndex].NameHash;
        }

        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")!.Type : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<ArrayHashAttribute>()?.Hash ?? uint.MaxValue;
        }

        return type.GetCustomAttribute<NameHashAttribute>()?.Hash ?? uint.MaxValue;
    }

    internal static void ReadCache(DataStream inStream)
    {
        int count = inStream.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            TypeInfoAsset type = new(inStream.ReadNullTerminatedString(), inStream.ReadUInt32(), inStream.ReadGuid());
            const int flag = 1 << 31;
            int index = s_typeInfoAssets.Count | flag;

            if (!string.IsNullOrEmpty(type.Name))
            {
                s_nameMapping.Add(type.Name, index);
            }

            s_guidMapping.Add(type.Guid, index);

            if (type.NameHash != uint.MaxValue)
            {
                s_nameHashMapping.Add(type.NameHash, index);
            }

            s_typeInfoAssets.Add(type);
        }
    }

    internal static void WriteCache(DataStream inStream)
    {
        long pos = inStream.Position;
        inStream.WriteUInt32(0xdeadbeef);

        int count = 0;
        foreach (TypeInfoAsset type in s_typeInfoAssets)
        {
            count++;

            inStream.WriteNullTerminatedString(type.Name);
            inStream.WriteUInt32(type.NameHash);
            inStream.WriteGuid(type.Guid);
        }
        inStream.StepIn(pos);
        inStream.WriteInt32(count);
        inStream.StepOut();
    }
}
}