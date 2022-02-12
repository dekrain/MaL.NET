namespace mal.Types
{
	public interface IInvocable
	{
		public Value Invoke(Interpreter interpreter, params Value[] args);
	}
}
