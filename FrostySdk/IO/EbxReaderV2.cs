using FrostySdk.Attributes;
using FrostySdk.Ebx;
using FrostySdk.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;

namespace FrostySdk.IO
{
    public class EbxReaderV2 : EbxReader
    {
        public override string RootType {
            get {
                Type type = TypeLibrary.GetType(classGuids[instances[0].ClassRef]);
                return type != null ? type.Name : "";
            }
        }

        internal List<Guid> classGuids = new List<Guid>();
        private HashSet<Guid> classGuidSet = new HashSet<Guid>();

        internal static EbxSharedTypeDescriptors std = null;
        internal static EbxSharedTypeDescriptors patchStd = null;
        internal static bool patched = false;

        private static readonly Dictionary<Type, TypeInfoGuidAttribute[]> s_typeGuidAttrCache
            = new Dictionary<Type, TypeInfoGuidAttribute[]>();

        private static readonly Dictionary<Type, EbxClassMetaAttribute> s_classMetaCache
            = new Dictionary<Type, EbxClassMetaAttribute>();

        private static readonly Dictionary<Type, Dictionary<uint, PropertyInfo>> s_hashPropCache
            = new Dictionary<Type, Dictionary<uint, PropertyInfo>>();

        private static readonly Dictionary<Type, PropEntry[]> s_classPropCache
            = new Dictionary<Type, PropEntry[]>();

        private static readonly Dictionary<Type, Action<object, AssetClassGuid>> s_setGuidCache
            = new Dictionary<Type, Action<object, AssetClassGuid>>();

        private static readonly object s_cacheLock = new object();

        private readonly struct PropEntry
        {
            public readonly Func<object, object> Getter;
            public readonly Action<object, object> Setter;
            public readonly EbxFieldMetaAttribute Meta;
            public readonly Type PropertyType;
            public readonly bool IsReference;

            public PropEntry(Func<object, object> getter, Action<object, object> setter,
                             EbxFieldMetaAttribute meta, Type propertyType, bool isRef)
            {
                Getter = getter;
                Setter = setter;
                Meta = meta;
                PropertyType = propertyType;
                IsReference = isRef;
            }
        }

        internal EbxReaderV2(Stream inStream) : base(inStream, true) { }

        public EbxReaderV2(Stream InStream, bool inPatched) : base(InStream, true)
        {
            if (std == null && FileSystemManager.HasFileInMemoryFs("SharedTypeDescriptors.ebx"))
            {
                std = new EbxSharedTypeDescriptors("SharedTypeDescriptors.ebx");
                if (FileSystemManager.HasFileInMemoryFs("SharedTypeDescriptors_patch.ebx"))
                    patchStd = new EbxSharedTypeDescriptors("SharedTypeDescriptors_patch.ebx");
            }

            patched = inPatched;
            magic = (EbxVersion)ReadUInt();
            if (magic != EbxVersion.Version2 && magic != EbxVersion.Version4)
                return;

            stringsOffset = ReadUInt();
            stringsAndDataLen = ReadUInt();
            guidCount = ReadUInt();
            instanceCount = ReadUShort();
            exportedCount = ReadUShort();
            uniqueClassCount = ReadUShort();
            classTypeCount = ReadUShort();
            fieldTypeCount = ReadUShort();
            typeNamesLen = ReadUShort();

            stringsLen = ReadUInt();
            arrayCount = ReadUInt();
            dataLen = ReadUInt();

            arraysOffset = stringsOffset + stringsLen + dataLen;
            fileGuid = ReadGuid();

            boxedValuesCount = ReadUInt();
            boxedValuesOffset = ReadUInt() + stringsOffset + stringsLen;

            for (int i = 0; i < guidCount; i++)
            {
                EbxImportReference import = new EbxImportReference
                {
                    FileGuid = ReadGuid(),
                    ClassGuid = ReadGuid()
                };
                imports.Add(import);
                if (!dependencies.Contains(import.FileGuid))
                    dependencies.Add(import.FileGuid);
            }

            Dictionary<int, string> typeNames = new Dictionary<int, string>();
            long typeNamesOffset = Position;
            while (Position - typeNamesOffset < typeNamesLen)
            {
                string typeName = ReadNullTerminatedString();
                int hash = HashString(typeName);
                if (!typeNames.ContainsKey(hash))
                    typeNames.Add(hash, typeName);
            }

            for (int i = 0; i < fieldTypeCount; i++)
            {
                EbxField fieldType = new EbxField();
                int hash = ReadInt();
                fieldType.Type = (magic == EbxVersion.Version2) ? ReadUShort() : (ushort)(ReadUShort() >> 1);
                fieldType.ClassRef = ReadUShort();
                fieldType.DataOffset = ReadUInt();
                fieldType.SecondOffset = ReadUInt();
                fieldType.Name = typeNames[hash];
                fieldTypes.Add(fieldType);
            }

            for (int i = 0; i < classTypeCount; i++)
            {
                Guid g = ReadGuid();
                classGuids.Add(g);
                classGuidSet.Add(g);
            }

            uint tmpExportedCount = exportedCount;
            for (int i = 0; i < instanceCount; i++)
            {
                EbxInstance inst = new EbxInstance
                {
                    ClassRef = ReadUShort(),
                    Count = ReadUShort()
                };
                if (tmpExportedCount != 0) { inst.IsExported = true; tmpExportedCount--; }
                instances.Add(inst);
            }

            while (Position % 16 != 0) Position++;

            for (int i = 0; i < arrayCount; i++)
                arrays.Add(new EbxArray { Offset = ReadUInt(), Count = ReadUInt(), ClassRef = ReadInt() });

            Pad(16);

            for (int i = 0; i < boxedValuesCount; i++)
                boxedValues.Add(new EbxBoxedValue { Offset = ReadUInt(), ClassRef = ReadUShort(), Type = ReadUShort() });

            Position = stringsOffset + stringsLen;
            isValid = true;
        }

        internal override void InternalReadObjects()
        {
            foreach (EbxInstance inst in instances)
            {
                Type objType = TypeLibrary.GetType(classGuids[inst.ClassRef]);
                for (int i = 0; i < inst.Count; i++)
                {
                    objects.Add(TypeLibrary.CreateObject(objType));
                    refCounts.Add(0);
                }
            }

            int typeId = 0, index = 0;
            foreach (EbxInstance inst in instances)
            {
                for (int i = 0; i < inst.Count; i++)
                {
                    object obj = objects[typeId++];
                    Type objType = obj.GetType();
                    EbxClass classType = GetClass(objType);

                    while (Position % classType.Alignment != 0) Position++;

                    Guid instanceGuid = inst.IsExported ? ReadGuid() : Guid.Empty;
                    if (classType.Alignment != 0x04) Position += 8;

                    GetSetGuidDelegate(objType)?.Invoke(obj, new AssetClassGuid(instanceGuid, index++));
                    ReadClass(classType, obj, Position - 8);
                }
            }
        }

        internal EbxClass GetClass(Type objType)
        {
            TypeInfoGuidAttribute[] attrs = GetTypeGuidAttributes(objType);
            EbxClass? classType = null;

            if (patchStd != null)
            {
                foreach (TypeInfoGuidAttribute attr in attrs)
                {
                    if (classGuidSet.Contains(attr.Guid))
                    {
                        classType = patchStd.GetClass(attr.Guid);
                        if (classType.HasValue) return classType.Value;
                    }
                }
            }

            foreach (TypeInfoGuidAttribute attr in attrs)
            {
                if (classGuidSet.Contains(attr.Guid))
                {
                    classType = std.GetClass(attr.Guid);
                    if (classType.HasValue) break;
                }
            }

            return classType.Value;
        }

        internal override PropertyInfo GetProperty(Type objType, EbxField field)
        {
            if (field.NameHash == 0xb95a6ae7) return null;

            if (!s_hashPropCache.TryGetValue(objType, out var map))
            {
                lock (s_cacheLock)
                {
                    if (!s_hashPropCache.TryGetValue(objType, out map))
                    {
                        map = new Dictionary<uint, PropertyInfo>();
                        foreach (PropertyInfo pi in objType.GetProperties())
                        {
                            HashAttribute attr = pi.GetCustomAttribute<HashAttribute>();
                            if (attr != null) map[(uint)attr.Hash] = pi;
                        }
                        s_hashPropCache[objType] = map;
                    }
                }
            }

            map.TryGetValue(field.NameHash, out PropertyInfo res);
            return res;
        }

        internal override EbxClass GetClass(EbxClass? classType, int index)
        {
            EbxClass? newClassType = null;
            Guid? guid = null;

            if (!classType.HasValue)
            {
                guid = classGuids[index];
                newClassType = patchStd?.GetClass(guid.Value);
                if (!newClassType.HasValue)
                    newClassType = std.GetClass(guid.Value);
            }
            else
            {
                int idx = (short)index + classType.Value.Index;
                guid = std.GetGuid(idx);

                if (classType.Value.SecondSize == 1)
                {
                    guid = patchStd.GetGuid(idx);
                    newClassType = patchStd.GetClass(idx);
                    if (!newClassType.HasValue)
                        newClassType = std.GetClass(guid.Value);
                }
                else
                {
                    newClassType = std.GetClass(idx);
                }
            }

            if (newClassType.HasValue)
                TypeLibrary.AddType(newClassType.Value.Name, guid);

            return newClassType.Value;
        }

        internal override EbxField GetField(EbxClass classType, int index) =>
            classType.SecondSize == 1 ? patchStd.GetField(index).Value : std.GetField(index).Value;

        internal override object CreateObject(EbxClass classType) => std != null
            ? TypeLibrary.CreateObject(classType.SecondSize == 1
                ? patchStd.GetGuid(classType).Value
                : std.GetGuid(classType).Value)
            : TypeLibrary.CreateObject(classType.Name);

        internal override Type GetType(EbxClass classType) =>
            TypeLibrary.GetType(classType.SecondSize == 1
                ? patchStd.GetGuid(classType).Value
                : std.GetGuid(classType).Value);

        internal virtual object ReadClass(EbxClassMetaAttribute classMeta, object obj, Type objType, long startOffset)
        {
            if (obj == null)
            {
                Position += classMeta.Size;
                while (Position % classMeta.Alignment != 0) Position++;
                return null;
            }

            if (objType.BaseType != typeof(object))
                ReadClass(GetClassMeta(objType.BaseType), obj, objType.BaseType, startOffset);

            PropEntry[] props = GetPropEntries(objType);

            foreach (PropEntry entry in props)
            {
                Position = startOffset + entry.Meta.Offset;

                if (entry.Meta.Type == EbxFieldType.Array)
                {
                    int arrIdx = ReadInt();
                    EbxArray arr = arrays[arrIdx];
                    long savedPos = Position;
                    Position = arraysOffset + arr.Offset;

                    IList list = entry.Getter?.Invoke(obj) as IList;
                    if (list != null)
                    {
                        list.Clear();
                        for (int i = 0; i < arr.Count; i++)
                            list.Add(ReadField(entry.Meta.ArrayType, entry.Meta.BaseType, entry.IsReference));
                    }

                    Position = savedPos;
                }
                else
                {
                    entry.Setter?.Invoke(obj, ReadField(entry.Meta.Type, entry.PropertyType, entry.IsReference));
                }
            }

            while (Position - startOffset != classMeta.Size) Position++;
            return null;
        }

        internal object ReadField(EbxFieldType type, Type baseType, bool dontRefCount = false)
        {
            switch (type)
            {
                case EbxFieldType.Boolean: return ReadBoolean();
                case EbxFieldType.Int8: return (sbyte)ReadByte();
                case EbxFieldType.UInt8: return ReadByte();
                case EbxFieldType.Int16: return ReadShort();
                case EbxFieldType.UInt16: return ReadUShort();
                case EbxFieldType.Int32: return ReadInt();
                case EbxFieldType.UInt32: return ReadUInt();
                case EbxFieldType.Int64: return ReadLong();
                case EbxFieldType.UInt64: return ReadULong();
                case EbxFieldType.Float32: return ReadFloat();
                case EbxFieldType.Float64: return ReadDouble();
                case EbxFieldType.Guid: return ReadGuid();
                case EbxFieldType.ResourceRef: return ReadResourceRef();
                case EbxFieldType.Sha1: return ReadSha1();
                case EbxFieldType.String: return ReadSizedString(32);
                case EbxFieldType.CString: return ReadCString(ReadUInt());
                case EbxFieldType.FileRef: return ReadFileRef();
                case EbxFieldType.TypeRef: return ReadTypeRef();
                case EbxFieldType.BoxedValueRef: return ReadBoxedValueRef();

                case EbxFieldType.Struct:
                    {
                        object structObj = TypeLibrary.CreateObject(baseType);
                        Type structType = structObj.GetType();
                        EbxClassMetaAttribute sMeta = GetClassMeta(structType);
                        Pad(sMeta.Alignment);
                        ReadClass(sMeta, structObj, structType, Position);
                        return structObj;
                    }

                case EbxFieldType.Enum: return ReadInt();
                case EbxFieldType.Pointer: return ReadPointerRef(dontRefCount);
                case EbxFieldType.DbObject: throw new InvalidDataException("DbObject");
                default: throw new InvalidDataException("Unknown");
            }
        }

        private static Action<object, AssetClassGuid> GetSetGuidDelegate(Type objType)
        {
            if (s_setGuidCache.TryGetValue(objType, out var del)) return del;
            lock (s_cacheLock)
            {
                if (s_setGuidCache.TryGetValue(objType, out del)) return del;
                MethodInfo mi = objType.GetMethod("SetInstanceGuid");
                if (mi != null)
                {
                    var tParam = Expression.Parameter(typeof(object), "t");
                    var vParam = Expression.Parameter(typeof(AssetClassGuid), "v");
                    del = Expression.Lambda<Action<object, AssetClassGuid>>(
                        Expression.Call(Expression.Convert(tParam, objType), mi, vParam),
                        tParam, vParam
                    ).Compile();
                }
                s_setGuidCache[objType] = del;
                return del;
            }
        }

        private static TypeInfoGuidAttribute[] GetTypeGuidAttributes(Type type)
        {
            if (s_typeGuidAttrCache.TryGetValue(type, out var attrs)) return attrs;
            lock (s_cacheLock)
            {
                if (s_typeGuidAttrCache.TryGetValue(type, out attrs)) return attrs;
                object[] raw = type.GetCustomAttributes(typeof(TypeInfoGuidAttribute), false);
                attrs = new TypeInfoGuidAttribute[raw.Length];
                for (int i = 0; i < raw.Length; i++) attrs[i] = (TypeInfoGuidAttribute)raw[i];
                s_typeGuidAttrCache[type] = attrs;
                return attrs;
            }
        }

        private static EbxClassMetaAttribute GetClassMeta(Type type)
        {
            if (s_classMetaCache.TryGetValue(type, out var meta)) return meta;
            lock (s_cacheLock)
            {
                if (s_classMetaCache.TryGetValue(type, out meta)) return meta;
                meta = type.GetCustomAttribute<EbxClassMetaAttribute>();
                s_classMetaCache[type] = meta;
                return meta;
            }
        }

        private static PropEntry[] GetPropEntries(Type objType)
        {
            if (s_classPropCache.TryGetValue(objType, out var entries)) return entries;
            lock (s_cacheLock)
            {
                if (s_classPropCache.TryGetValue(objType, out entries)) return entries;

                var list = new List<PropEntry>();
                var tParam = Expression.Parameter(typeof(object), "t");
                var vParam = Expression.Parameter(typeof(object), "v");

                foreach (PropertyInfo pi in objType.GetProperties(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (pi.GetCustomAttribute<IsTransientAttribute>() != null) continue;

                    EbxFieldMetaAttribute meta = pi.GetCustomAttribute<EbxFieldMetaAttribute>();
                    if (meta == null) continue;

                    bool isRef = pi.GetCustomAttribute<IsReferenceAttribute>() != null;

                    Func<object, object> getter = Expression.Lambda<Func<object, object>>(
                        Expression.Convert(
                            Expression.Property(Expression.Convert(tParam, objType), pi),
                            typeof(object)
                        ), tParam
                    ).Compile();

                    Action<object, object> setter = null;
                    if (pi.CanWrite && pi.SetMethod != null)
                    {
                        setter = Expression.Lambda<Action<object, object>>(
                            Expression.Call(
                                Expression.Convert(tParam, objType),
                                pi.SetMethod,
                                Expression.Convert(vParam, pi.PropertyType)
                            ), tParam, vParam
                        ).Compile();
                    }

                    list.Add(new PropEntry(getter, setter, meta, pi.PropertyType, isRef));
                }

                entries = list.ToArray();
                s_classPropCache[objType] = entries;
                return entries;
            }
        }
    }
}