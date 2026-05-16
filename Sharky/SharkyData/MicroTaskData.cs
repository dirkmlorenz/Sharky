namespace Sharky
{
    public class MicroTaskData : Dictionary<string, IMicroTask>
    {
        /// <summary>
        /// Removes the unit from all unit commanders of all microtasks.
        /// Does not change unit role or claimed.
        /// </summary>
        /// <param name="commander"></param>
        public void StealCommanderFromAllTasks(UnitCommander commander)
        {
            foreach (var microTask in this)
            {
                microTask.Value.StealUnit(commander);
            }
        }

        /// <summary>
        /// Assigns a new micro controller for a unit in the attack task
        /// </summary>
        public void UpdateAttackMicroController(UnitTypes unitType, IIndividualMicroController microController)
        {
            if (this.GetValueOrDefault("AttackTask") is AttackTask attackTask)
            {
                attackTask.UpdateUnitMicroController(unitType, microController);
            }
        }
    }
}
