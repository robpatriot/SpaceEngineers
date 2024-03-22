using System;

// Space Engineers DLLs
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using VRageMath;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Numerics;

/*
 * Must be unique per each script project.
 * Prevents collisions of multiple `class Program` declarations.
 * Will be used to detect the ingame script region, whose name is the same.
 */
namespace MoveTest
{

    /*
     * Do not change this declaration because this is the game requirement.
     */
    public sealed class Program : MyGridProgram
    {

        /*
         * Must be same as the namespace. Will be used for automatic script export.
         * The code inside this region is the ingame script.
         */
        #region MoveTest

        MyCommandLine commandLine = new MyCommandLine();
        // One of the available ship controllers or null if none
        IMyShipController shipController;

        // List of thrusters
        List<IMyThrust> thrusters = new List<IMyThrust>();
        // ship velocity
        Vector3D velocity { get { return shipController.GetShipVelocities().LinearVelocity; } }
        // ship center of mass (so rotation doesn't make us change position)
        Vector3D position { get { return shipController.CenterOfMass; } }
        // ship gravity, including any artificial gravity fields
        Vector3D gravity { get { return shipController.GetTotalGravity(); } }
        // physical mass includes multipliers etc.
        double mass { get { return shipController.CalculateShipMass().PhysicalMass; } }
        // Where do we want to go
        Vector3D targetPosition;
        // Group the thrusters by cardinal direction on the ship
        Dictionary<Vector3D, List<IMyThrust>> thrusterGroups = new Dictionary<Vector3D, List<IMyThrust>>();

        // GroupThrust contains the intermediate values for the thrust calculation
        // for a group of thrusters which are all facing the same direction
        class GroupThrust
        {
            // List of thrusters in this group
            List<IMyThrust> thrusters = new List<IMyThrust>();

            // max effective thrust of all thrusters in this group in newtons
            double maxEffectiveThrust = 0.0;

            // calibration factor is the thrust contribution of this group that results in 
            // equal acceleration in all active directions
            double calibrationFactor = 1.0;

            // contribution of this group to the thrust in the desired direction
            double contribution = 0.0;

            // backward direction of the group in world coordinates
            Vector3D wBackward
            {
                get
                {
                    if (thrusters.Count == 0) return Vector3D.Zero;
                    return thrusters[0].WorldMatrix.Backward;
                }
            }
        }

        /*
         * The constructor, called only once every session and always before any 
         * other method is called. Use it to initialize your script. 
         *    
         * It's recommended to set RuntimeInfo.UpdateFrequency here, which will 
         * allow your script to run itself without a timer block.
         */
        public Program() { 
            // All this initialisation could be done per tick to be resilient to changes in the ship
            shipController = ShipController();

            GridTerminalSystem.GetBlocksOfType<IMyThrust>(thrusters, thruster => thruster.IsWorking);
            
            // Group thrusters in cardinal direction
            CreateThrusterGroups();
        }

        /*
         * Get a control point for this ship
         */
        private IMyShipController ShipController()
        {
            if (shipController == null || !shipController.IsWorking)
            {
                List<IMyShipController> shipControllers = new List<IMyShipController>();
                GridTerminalSystem.GetBlocksOfType<IMyShipController>(shipControllers, controller => controller.CanControlShip);

                if (shipControllers.Count > 0) { shipController = shipControllers[0]; }
                else { shipController = null; }
            }

            return shipController;
        }

        // Creates the groups of thrusters by all six cardinal directions on the ship
        public void CreateThrusterGroups()
        {
            // Empty any previous groups
            thrusterGroups.Clear();

            foreach (var thruster in thrusters)
            {
                var dir = thruster.GridThrustDirection;

                // Check the group already exists for this direction, if not create it
                if (!thrusterGroups.ContainsKey(dir))
                    thrusterGroups[dir] = new List<IMyThrust>();

                // Add the thruster to the group
                thrusterGroups[dir].Add(thruster);
            }
        }

        public void ApplyThrust(Dictionary<Vector3D, GroupThrust> active, double throttle)
        {
            // If we want the ship to respond linearly to throttle we need to apply
            // the square root of the throttle factor
            double rootThrottle = Math.Sqrt(throttle);

            foreach (var group in active)
            {
                foreach (var thruster in group.Value.thrusters)
                {
                    // Thrust override uses MaxThrust so we need to convert from effective thrust
                    // TODO is this upside down?
                    var thrustOverride = thruster.MaxThrust / thruster.MaxEffectiveThrust;

                    // set the thrust override based on:
                    //  - the max effective thrust of the thruster
                    //  - the calibration factor which evens out the max thrust from all active directions
                    //  - the contribution of the thruster in the desired direction
                    //  - the throttle factor
                    //  - the conversion factor from effective thrust to max thrust for the override
                    thruster.ThrustOverride = (float)(thruster.MaxEffectiveThrust * group.Value.calibrationFactor * group.Value.contribution * rootThrottle * thrustOverride);
                }
            }
        }

        // Move in the direction of the given world position with the given throttle factor
        //
        // This is an example of a method that could be used to move the ship towards a
        // given world position with a given throttle factor.
        //
        // This ignores the current velocity and gravity, but you could add them in.
        //
        // wPos: The world position to move towards
        // throttle: The throttle factor to use
        public void MoveInDirectionOf(Vector3D wPos, double throttle)
        {
            // Get a unit vector direction toward the target world position
            Vector3D uDir = Vector3D.ClampToSphere(wPos - position, 1);

            // Determine the group with the minimum max effective thrust in the loop below
            double minMaxEffectiveThrust = double.MaxValue;

            // Determine which groups of thrusters are active in the desired direction
            Dictionary<Vector3D, GroupThrust> active = new Dictionary<Vector3D, GroupThrust>();

            foreach (var group in thrusterGroups)
            {
                double contribution = Vector3D.Dot(group.wBackward, uDir);

                if (contribution > 0)
                {
                    active[group.wBackward] = new GroupThrust();
                    active[group.wBackward].thrusters = group.Value;
                    active[group.wBackward].contribution = contribution;

                    // Calculate the max effective thrust for this group
                    foreach (var thruster in group.Value)
                        active[group.wBackward].maxEffectiveThrust += thruster.MaxEffectiveThrust;

                    // Update the minimum max effective thrust
                    if (active[group.wBackward].maxEffectiveThrust < minMaxEffectiveThrust)
                        minMaxEffectiveThrust = active[group.wBackward].maxEffectiveThrust;
                }
            }

            // Calculate the factor for each group of thrusters which would result in equal
            // acceleration in all active directions
            foreach (var group in active)
            {
                group.Value.calibrationFactor = minMaxEffectiveThrust / group.Value.maxEffectiveThrust;
            }

            // Apply the thrust to the active groups
            ApplyThrust(active, throttle);
        }

        /*
        * Shut everything down to give control back to the player 
        */
        private void Stop()
        {
            foreach (var thruster in thrusters) { thruster.ThrustOverride = (float)0.0; }
        }

        /*
         * Called when the program needs to save its state. Use this method to save
         * your state to the Storage field or some other means. 
         */
        public void Save() { }

        public void Main(string argument, UpdateType updateSource)
        {
            if (shipController == null) { Echo("This ship has no working controllers"); return; }
            if (thrusters.Count == 0) { Echo("This ship has no working thrusters"); return; }

            if (commandLine.TryParse(argument))
            {
                string command = commandLine.Argument(0).ToLower();

                switch (command)
                {
                    case "start":
                        shipController.DampenersOverride = false;
                        // Create an arbitrary move to complete for now
                        targetPosition = position;
                        targetPosition.Z += 1000;
                        MoveInDirectionOf(targetPosition, 1);
                        // Register ourselves for every 100 ticks update
                        // Runtime.UpdateFrequency = UpdateFrequency.Update100;
                        break;
                    case "stop":
                        shipController.DampenersOverride = true;
                        Stop();
                        // Stop registering for tick updates
                        Runtime.UpdateFrequency = UpdateFrequency.None;
                        break;
                    default:
                        Echo("Got unknown command: " + command);
                        break;
                }
            }
            else
            {
                // We got called with no argument process regular tick
            }
        }

        #endregion // MoveTest
    }
}