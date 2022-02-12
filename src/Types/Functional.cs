using System;

namespace mal.Types
{
	// Functional extensions for MaL types
	public static class FunctionalExtensions
	{
		public static ListBase Map(this ListBase list, Func<Value, Value> mapper)
		{
			return list.Top == null ? list : new ListBase(list.Top.Map(mapper), isVector: list.IsVector);
		}

		public static ListNode Map(this ListNode node, Func<Value, Value> mapper)
		{
			return new ListNode(mapper(node.Head), node.Tail?.Map(mapper));
		}

		public static Value[] ToArray(this ListNode? node)
		{
			if (node == null)
				return Array.Empty<Value>();
			var list = new Value[node.Count];
			int idx = 0;
			foreach (var item in node)
				list[idx++] = item;
			return list;
		}
	}
}
