
namespace FDG.Data.Commands
{
    internal class SetValueCommand<T> : IGameCommand
    {

        private DataReference _reference;
        
        private T _value;

        public SetValueCommand(DataReference reference, T value)
        {
            _reference = reference;
            _value = value;
        }

        public void Execute(CommandProcessor processor)
        {
            processor.SetValue(_reference, _value);
        }

        
    }
}
