using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace mal.Types
{
	public class ListBase : Value, IEnumerable<Value>
	{
		public static ListBase Empty => new ListBase();
		public static ListBase EmptyVector => new ListBase(isVector: true);

		public ListBase(ListNode? top = null, bool isVector = false)
		{
			Top = top;
			IsVector = isVector;
		}

		public readonly ListNode? Top;
		public readonly bool IsVector;

		public bool IsInterned => Top?.IsInterned ?? true;

		public IEnumerator<Value> GetEnumerator() => new ListEnumerator(Top);
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		public int Count => Top.GetCount();

		protected override bool EqualsImpl(Value other) => EqualsImpl((ListBase)other);
		private bool EqualsImpl(ListBase other) => IsVector == other.IsVector && Top == other.Top;

		public override ValueTypeCode TypeCode => IsVector ? ValueTypeCode.Vector : ValueTypeCode.List;
	}

	public class ListInterner
	{
		struct NodeDescriptor : IEquatable<NodeDescriptor>
		{
			public WeakReference<Value> car;
			public WeakReference<ListNode?> cdr;

			public bool Equals(NodeDescriptor other)
			{
				//
				// See the full list of guidelines at
				//   http://go.microsoft.com/fwlink/?LinkID=85237
				// and also the guidance for operator== at
				//   http://go.microsoft.com/fwlink/?LinkId=85238
				//

				car.TryGetTarget(out var my_car);
				cdr.TryGetTarget(out var my_cdr);
				other.car.TryGetTarget(out var other_car);
				other.cdr.TryGetTarget(out var other_cdr);

				return (my_car?.MetaEquals(other_car) ?? false) && my_cdr == other_cdr;
			}

			public override int GetHashCode()
			{
				this.car.TryGetTarget(out var car);
				this.cdr.TryGetTarget(out var cdr);
				return (car, cdr).GetHashCode();
			}
		}

		private readonly Dictionary<NodeDescriptor, WeakReference<ListNode>> nodes = new();

		public bool FindCons(Value car, ListNode? cdr, [MaybeNullWhen(false)] out ListNode found)
		{
			if (nodes.TryGetValue(new NodeDescriptor{ car = new(car), cdr = new(cdr) }, out var ref_found))
				return ref_found.TryGetTarget(out found);

			found = null;
			return false;
		}

		public ListNode InsertCons(ListNode node)
		{
			nodes.Add(new NodeDescriptor{ car = new(node.Head), cdr = new(node.Tail) }, new(node));
			return node;
		}

		public void DumpState()
		{
			Console.WriteLine("List interner:");
			foreach(var entry in nodes)
			{
				entry.Value.TryGetTarget(out var x);
				entry.Key.car.TryGetTarget(out var y);
				if (x == null)
				{
					Console.WriteLine($"Has uncleaned entry ({(y == null ? "no key" : "has key reference")})");
				}
			}
		}
	}

	public class ListNode : IEquatable<ListNode>
	{
		Value value;
		ListNode? next;
		ListInterner? interner;

		public ListNode(Value value, ListNode? next = null)
		{
			this.value = value;
			this.next = next;
		}

		public static ListNode ConsIntern(ListInterner interner, Value value, ListNode? next = null)
		{
			if (interner.FindCons(value, next, out var found))
				return found;
			return interner.InsertCons(new ListNode(value, next) { interner = interner });
		}

		public static ListNode MaybeConsIntern(ListInterner interner, out bool interned, Value value, ListNode? next = null)
		{
			if (interned = interner.FindCons(value, next, out var found))
				return found!;
			return new ListNode(value, next);
		}

		internal ListNode RecurseIntern(ListInterner interner)
		{
			ListNode? new_cdr = Tail?.RecurseIntern(interner);
			return ConsIntern(interner, Head, new_cdr);
		}

		internal ListNode MaybeRecurseIntern(ListInterner interner)
		{
			ListNode? new_cdr = Tail?.MaybeRecurseIntern(interner);
			if (object.ReferenceEquals(new_cdr, Tail))
				return this;
			return MaybeConsIntern(interner, out bool interned, Head, new_cdr);
		}

		//public IEnumerator<Value> GetEnumerator() => new ListEnumerator(this);
		//IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		public Value Head => value;
		public ListNode? Tail => next;
		public bool IsInterned => interner != null;

		public int Count => this.GetCount();

		internal void SetTail(ListNode? tail)
		{
			next = tail;
		}

		public bool Equals(ListNode? other)
		{
			return other != null && Head == other.Head && Tail == other.Tail;
		}

		public static bool operator==(ListNode? lhs, ListNode? rhs) => lhs?.Equals(rhs) ?? rhs is null;
		public static bool operator!=(ListNode? lhs, ListNode? rhs) => !(lhs == rhs);
	}

	public static class ListExtensions
	{
		public static ListEnumerator GetEnumerator(this ListNode? node) => new ListEnumerator(node);
		public static ListEnumeratorWithSkips GetSubListEnumerator(this ListNode? node, int skips) => new ListEnumeratorWithSkips(node, skips);
		public static SubListEnumerator GetSubListEnumerator(this ListBase list, int skips) => new SubListEnumerator(list, skips);
		public static int GetCount(this ListNode? node)
		{
			int count = 0;
			for (; node != null; node = node.Tail)
				++count;
			return count;
		}
	}

	public class ListEnumerator : IEnumerator<Value>, IEnumerable<Value>
	{
		private bool started = false;
		protected ListNode? list;

		public ListEnumerator(ListNode? list = null)
		{
			this.list = list;
		}

		public virtual Value Current
		{
			get
			{
				if (list == null)
					throw new InvalidOperationException();
				return list.Head;
			}
		}

		object IEnumerator.Current => Current;

		public bool MoveNext()
		{
			if (started)
				MoveNextElement();
			else
				started = true;
			return list != null;
		}

		protected virtual void MoveNextElement() => list = list?.Tail;

		public void Reset()
		{
			list = null;
		}

		public void Dispose()
		{}

		IEnumerator<Value> IEnumerable<Value>.GetEnumerator() => this;
		IEnumerator IEnumerable.GetEnumerator() => this;
	}

	public class ListEnumeratorWithSkips : ListEnumerator, IEnumerable<ListNode>, IEnumerator<ListNode>
	{
		private int numSkips;

		public ListEnumeratorWithSkips(ListNode? list, int skips)
			: base(list)
		{
			numSkips = skips;
		}

		ListNode IEnumerator<ListNode>.Current
		{
			get
			{
				if (list == null)
					throw new InvalidOperationException();
				return list;
			}
		}

		protected override void MoveNextElement()
		{
			for (int idx = 0; idx < numSkips; ++idx)
				base.MoveNextElement();
		}

		IEnumerator<ListNode> IEnumerable<ListNode>.GetEnumerator() => this;
	}

	public class SubListEnumerator : ListEnumeratorWithSkips
	{
		private bool isVector;

		public SubListEnumerator(ListBase list, int skips)
			: base(list.Top, skips)
		{
			isVector = list.IsVector;
		}

		public override Value Current => new ListBase(list, isVector: isVector);
	}

	public class ListBuilder : IEnumerable
	{
		ListNode? head;
		ListNode? last;

		public void Add(Value element)
		{
			var node = new ListNode(element);
			if (head == null)
			{
				last = head = node;
			}
			else
			{
				last!.SetTail(node);
				last = node;
			}
		}

		public void AddRange(IEnumerable<Value> range)
		{
			foreach (var element in range)
				Add(element);
		}

		public ListBase Finish(bool asVector)
		{
			var list = head;
			last = head = null;
			return new ListBase(list, asVector);
		}

		public ListBase FinishIntern(ListInterner interner, bool asVector)
		{
			var list = head?.RecurseIntern(interner);
			last = head = null;
			return new ListBase(list, asVector);
		}

		public ListBase FinishMaybeIntern(ListInterner interner, bool asVector)
		{
			var list = head?.MaybeRecurseIntern(interner);
			last = head = null;
			return new ListBase(list, asVector);
		}

		// Only here to allow collection initializer syntax
		IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
	}
}
