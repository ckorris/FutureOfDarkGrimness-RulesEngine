
using System;
using System.Collections.Generic;
using FDG.Stages;
using System.Linq;


namespace FDG.Samples
{
    public class RangedAttackSimulator
    {
        private ITextOutput _textOutput;
        private IDiceRoller _diceRoller;


        private IRangedContext _rangedContext;
        private StateMachine _stateMachine;
        private ShootStage _shootStage;

        public RangedAttackSimulator(ERandomnessType randomnessType)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = GetDiceRoller(randomnessType);

            CreateStateMachine();
        }

        public RangedAttackSimulator(ITextOutput textOutput, ERandomnessType randomnessType)
        {
            _textOutput = textOutput;
            _diceRoller = GetDiceRoller(randomnessType);

            CreateStateMachine();
        }

        public RangedAttackSimulator(ITextOutput textOutput, IDiceRoller diceRoller)
        {
            _textOutput = textOutput;
            _diceRoller = diceRoller;

            CreateStateMachine();
        }

        public RangedAttackSimulator(IDiceRoller diceRoller)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = diceRoller;

            CreateStateMachine();
        }

        public void SimulateRangedAttack(IUnit attackingUnit, IUnit defendingUnit)
        {
            _rangedContext.BeginNewAttack(attackingUnit, new List<IUnit>() { defendingUnit });
            _stateMachine.Start(_shootStage);
        }

        private void CreateStateMachine()
        {
            BasicTesterChooseActionHandler _chooseActionHandler = new BasicTesterChooseActionHandler();
            BasicTesterMovementHandler _movementHandler = new BasicTesterMovementHandler();
            BasicTesterChooseRangedWeaponHandler _chooseWeaponHandler = new BasicTesterChooseRangedWeaponHandler();
            BasicTesterChooseRangedTargetHandler _chooseTargetHandler = new BasicTesterChooseRangedTargetHandler();

            _stateMachine = new StateMachine();
            IUnitActionContext unitActionContext = new UnitActionContext(_chooseActionHandler, _movementHandler,
                _textOutput, _diceRoller);
            _rangedContext = new RangedContext(_chooseWeaponHandler, _chooseTargetHandler, _textOutput, _diceRoller);

            _shootStage = new ShootStage(_stateMachine, unitActionContext, _rangedContext);
            _shootStage.AssignExitStage(new EmptyEndStage(_stateMachine));
        }

        private IDiceRoller GetDiceRoller(ERandomnessType randomnessType)
        {
            switch (randomnessType)
            {
                case ERandomnessType.Realistic:
                    return new RealisticDiceRoller();
                case ERandomnessType.Probabilistic:
                    return new ProbabilisticDiceRoller();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #region Dummy Handlers
        private class BasicTesterChooseActionHandler : IChooseActionHandler
        {
            public void Handle(IUnitActionContext context, Action chooseMovement, Action pass)
            {
                //For tests, we choose movement, because at least for now, that's how you attack - 
                //you choose your move and your attack at basically the same time, as how you move
                //affects your attack options.
                chooseMovement();
            }
        }

        private class BasicTesterMovementHandler : IMovementHandler
        {
            public void Handle(IUnitActionContext actionContext, Action onChooseMelee, Action onChooseRanged, Action onChooseNonCombat)
            {
                onChooseRanged();
            }
        }

        private class BasicTesterChooseRangedWeaponHandler : IChooseRangedWeaponHandler
        {
            public void Handle(IReadOnlyDictionary<IWeapon, int> availableWeapons, IReadOnlyDictionary<IWeapon, int> unavailableWeapons,
                Action<IWeapon> onChoseWeapon)
            {
                //Just choose the next weapon automatically.
                IWeapon firstWeapon = availableWeapons.First().Key;
                onChoseWeapon(firstWeapon);
            }
        }

        private class BasicTesterChooseRangedTargetHandler : IChooseRangedTargetHandler
        {
            public void Handle(IReadOnlyList<IUnit> potentialTargetUnits, Action<IUnit> onChoseUnit)
            {
                //Just choose the first.
                IUnit firstUnit = potentialTargetUnits.First();
                onChoseUnit(firstUnit);
            }
        }

        #endregion
    }
}