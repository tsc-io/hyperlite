# Serialization

The serializer behind `Hyperlite.Persistence`, and on its own in `Hyperlite.Serialization`: a
compact binary form that records enough about each value to read it back into a class that has
changed since it was written.

```csharp
var bytes = Serializer.ToByteArray(order);
var copy  = Deserializer.FromByteArray<Order>(bytes);
```

## What the bytes record

- **A tag per value**, and dedicated tags for common values - a zero, an empty string, `true` -
  which cost the tag alone.
- **The type** where the declaration does not settle it - a value behind a member typed `object`,
  an interface or a base class: by name, or by a registered type code.
- **A type schema** once per type per value written: the names of the members, in order. Each
  object then writes its member values in that order.
- **Back references** for a repeated object, string or type, so shared and cyclic graphs come
  back with their identity, and a repeat costs a reference rather than a copy.

Members are the public and non-public properties with a getter and a setter, or the fields of a
`[Serializable]` type. `[Transient]` or `[IgnoreDataMember]` leaves a member out entirely;
`[Ignore]` leaves it out only when the object is persisted as an entity.

## Robust against changes to the class

Reading matches stored members to the class by name, not by position, so a class can change
between writing and reading. Measured on what the serializer writes:

| change | what reading does |
|---|---|
| member added | not in the bytes: keeps what the constructor gave it |
| member removed | its stored value goes to `Resolver.ObsoletePropertyResolver`, and is dropped by default |
| member renamed | `[DataMember(Name = "...")]` keeps the stored name, or `Resolver.PropertyNameResolver` maps it |
| member type changed | converted where .NET can: numbers to wider or narrower ones, enums to and from their integers, anything to `string`, a string to a number, `T` to `T?`, a value to a one-element array |
| members reordered | nothing - names decide |
| case of a name changed | nothing - names match ignoring case |
| assembly version changed | nothing - version and culture are left out of stored names by default |
| written on .NET Framework, read on .NET, or back | the core library's different name is mapped |

A member renamed, keeping what is stored:

```csharp
public sealed class Customer
{
    [DataMember(Name = "Name")]          // was "Name", the bytes still say so
    public string DisplayName { get; set; }
}
```

The same without touching the class, and a removed member's value kept somewhere:

```csharp
Resolver.PropertyNameResolver = (type, stored) =>
    type == typeof(Customer) && stored == "Name" ? "DisplayName" : null;

Resolver.ObsoletePropertyResolver = (target, stored, value) =>
    log.Info($"{target.GetType().Name}.{stored} is gone, was {value}");
```

A type renamed or moved: the stored name no longer resolves, so map it in the binder.

```csharp
sealed class Renames : Binder
{
    public override Type BindToType(string assemblyName, string typeName) =>
        typeName == "Shop.Client" ? typeof(Shop.Customer) : base.BindToType(assemblyName, typeName);
}

Encoder.Configuration.Binder = new Renames();
```

## Smaller and faster with type codes

A type written by name costs its assembly-qualified name, once per value written - a back
reference for every repeat within it - and resolving that name when reading. A registered type
code replaces the name with a small number:

```csharp
// at startup, the same on every side that writes or reads - before the first value
Encoder.Register(1, typeof(Discount));
Encoder.Register(2, typeof(Voucher));
```

One `Discount` behind a member typed `object` took 103 bytes by name and 66 by code. Reading a
code needs no name resolution, and a type read by its code is allowed under strict types, because
registering it was explicit.

- **A code is part of the format.** Never reuse one for another type, and register it everywhere
  the data is read. A code the reader does not know reaches `Resolver.TypeCodeResolver`.
- **`Encoder.Configuration.UseBaseTypeCode`** writes a derived type under its base type's code
  when it has none of its own - for a hierarchy read as its base.
- **`Encoder.Configuration.StrictExplicitTypeCodes`** refuses to write any type by name, which
  proves that a format holds codes only.

## A serializer of your own

For a type the reflection path does not suit - immutable, with no setters, or where a compact or
versioned form of your own matters - register a serializer and a deserializer with its code:

```csharp
Encoder.Register(
    3,
    typeof(Money),
    (serializer, any) =>
    {
        var money = (Money)any;
        Encoder.WriteLong(serializer.BinaryWriter, money.Cents, false);
        Encoder.WriteString(serializer.BinaryWriter, money.Currency, false);
    },
    deserializer => new Money(
        Decoder.ReadLong(deserializer.BinaryReader),
        Decoder.ReadString(deserializer.BinaryReader)));
```

The `Encoder.Write*` and `Decoder.Read*` helpers write the format's own compact forms; `false`
leaves out the tag, since the reader knows what comes next. A value that is itself an object
goes through the serializer, so references and its own registration still apply:

```csharp
(serializer, any) => { var line = (Line)any; serializer.WriteObject(line.Product); }
deserializer => { var line = new Line(); deserializer.ReadObject<Product>(p => line.Product = p); return line; }
```

`ReadObject` takes a callback because the value may not exist yet: an entity it refers to may
still be on its way, and is delivered when it arrives.

A type implementing `ISerializable` is written through `GetObjectData` and read through its
`(SerializationInfo, StreamingContext)` constructor without any registration.

## Reading what you did not write

Reading is safe by default: `Encoder.Configuration.StrictTypes` is on, so only declared and
allowed types are created, and `EnableSerializationCallbacks` is off. A value behind a member
typed `object`, an interface or a base class needs its type allowed or registered with a code:

```csharp
Encoder.Configuration.AllowType(typeof(Discount));
```

Input nested too deeply, or stating more items than it holds, is refused whatever the settings.
`SECURITY.md` has the details.
