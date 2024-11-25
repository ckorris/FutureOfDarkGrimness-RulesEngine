using System;

namespace FDG
{
    public class ActionChoice
    {
        public readonly string ChoiceName;

        public readonly bool CanActivate;

        public readonly string ReasonCannotActivate;

        private readonly Action _onActivated;

        public ActionChoice(Action onActivated, string choiceName, bool canActivate, string reasonCannotActivate = null)
        {
            _onActivated = onActivated;
            ChoiceName = choiceName;
            CanActivate = canActivate;
            ReasonCannotActivate = reasonCannotActivate;
        }

        public void Choose()
        {
            if (CanActivate == false)
            {
                throw new InvalidOperationException($"Made a choice that's not available: {ChoiceName}");
            }

            _onActivated();
        }
    }
}
