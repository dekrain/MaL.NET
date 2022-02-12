using System;

namespace mal.Extensions.EnumExtensions
{
	public static class EnumExtensions
	{
		public static TAttr? GetCustomAttribute<TAttr, TEnum>(this TEnum en)
			where TAttr : Attribute
			where TEnum : struct, Enum
		{
			
			return (TAttr?)GetCustomAttribute<TEnum>(en, typeof(TAttr));
		}

		public static Attribute? GetCustomAttribute<TEnum>(this TEnum en, Type attrType)
			where TEnum : struct, Enum
		{
			var memberName = Enum.GetName<TEnum>(en);
			if (memberName is null)
				throw new ArgumentException("Given enum value doesn't have a name", nameof(en));
			var members = typeof(TEnum).GetMember(memberName);
			if (members.Length != 1)
				throw new InvalidOperationException("Enum member has multiple definitions");
			return Attribute.GetCustomAttribute(members[0], attrType);
		}

		public static bool HasCustomAttribute<TEnum>(this TEnum en, Type attrType)
			where TEnum : struct, Enum
			=> GetCustomAttribute<TEnum>(en, attrType) != null;
	}
}
