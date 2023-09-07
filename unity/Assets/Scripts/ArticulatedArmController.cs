using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;
using System.Linq;
public partial class ArticulatedArmController : ArmController {
    public ArticulatedArmJointSolver[] joints;
    
    [SerializeField]
    //this wrist placeholder represents the posrot manipulator's position on the IK Stretch so we can match the distance magnitudes
    //it is weird because the origin point of the last joint in the AB is in a different offset, so we have to account for that for benchmarking
    private Transform armBase, handCameraTransform, FirstJoint, wristPlaceholderTransform;

    private float wristPlaceholderForwardOffset;
 
    private PhysicsRemoteFPSAgentController PhysicsController;

    //Distance from joint containing gripper camera to armTarget
    private Vector3 WristToManipulator = new Vector3(0, -0.09872628f, 0);

    //held objects, don't need a reference to colliders since we are "attaching" via fixed joints instead of cloning
    public new List <SimObjPhysics> heldObjects;

    // TODO: Possibly reimplement this fucntions, if AB read of transform is ok then may not need to reimplement
    public override Transform pickupParent() {
        return magnetSphere.transform;
    }

    public override Vector3 wristSpaceOffsetToWorldPos(Vector3 offset) {
        return handCameraTransform.TransformPoint(offset) - handCameraTransform.position + WristToManipulator;
    }
    public override Vector3 armBaseSpaceOffsetToWorldPos(Vector3 offset) {
        return this.transform.TransformPoint(offset) - this.transform.position;
    }

    public override Vector3 pointToWristSpace(Vector3 point) {
        return handCameraTransform.TransformPoint(point) + WristToManipulator;
    }
    public override Vector3 pointToArmBaseSpace(Vector3 point) {
        return armBase.transform.TransformPoint(point);
    }

    public override void ContinuousUpdate(float fixedDeltaTime) {
        //so assume each joint that needs to move has had its `currentArmMoveParams` set
        //now we call `ControlJointFromAction` on all joints each physics update to get it to move...
        //Debug.Log("starting ArticulatedArmController.manipulateArm");
        foreach (ArticulatedArmJointSolver j in joints) {
            j.ControlJointFromAction(fixedDeltaTime);
        }
    }

    public override bool ShouldHalt() {
        //Debug.Log("checking ArticulatedArmController shouldHalt");
        bool shouldHalt = false;
        foreach (ArticulatedArmJointSolver j in joints) {
            //only halt if all joints report back that shouldHalt = true
            //joints that are idle and not moving will return shouldHalt = true by default
            //Debug.Log($"checking joint: {j.transform.name}");
            //Debug.Log($"distance moved so far for this joint is: {j.distanceMovedSoFar}");

            //check all joints that have had movement params set to see if they have halted or not
            if(j.currentArmMoveParams != null)
            {
                if (!j.shouldHalt(
                    distanceMovedSoFar: j.distanceMovedSoFar,
                    cachedPositions: j.currentArmMoveParams.cachedPositions,
                    tolerance: j.currentArmMoveParams.tolerance
                )) {
                    //if any single joint is still not halting, return false
                    //Debug.Log("still not done, don't halt yet");
                    shouldHalt = false;
                    return shouldHalt;
                }

                //this joint returns that it should stop! Now we must wait to see if there rest
                else
                {
                    //Debug.Log($"halted! Distance moved: {j.distanceMovedSoFar}");
                    shouldHalt = true;
                    continue;
                }
            }
        }

        //Debug.Log("halted, return true!");
        return shouldHalt;
    }

    public override void FinishContinuousMove(BaseFPSAgentController controller) {
            Debug.Log("starting continuousMoveFinishAB");
            bool actionSuccess = true;
            string debugMessage = "";

            controller.errorMessage = debugMessage;
            controller.actionFinished(actionSuccess, debugMessage);
    }

    public override GameObject GetArmTarget() {
        return armTarget.gameObject;
    }

    void Start() {
        wristPlaceholderForwardOffset = wristPlaceholderTransform.transform.localPosition.z;
        //Debug.Log($"wrist offset is: {wristPlaceholderForwardOffset}");

        // standingLocalCameraPosition = m_Camera.transform.localPosition;
        // Debug.Log($"------ AWAKE {standingLocalCameraPosition}");
        // this.collisionListener = this.GetComponentInParent<CollisionListener>();

        //TODO: Initialization

        // TODO: Replace Solver 
    }

    //TODO: main functions to reimplement, use continuousMovement.moveAB/rotateAB
    public override void moveArmRelative(
        PhysicsRemoteFPSAgentController controller,
        Vector3 offset,
        float unitsPerSecond,
        float fixedDeltaTime,
        bool returnToStart,
        string coordinateSpace,
        bool restrictTargetPosition,
        bool disableRendering
    ) {
        //not doing this one yet soooo uhhhhh ignore for now        
    }

    public void moveArmTarget(
        ArticulatedAgentController controller,
        Vector3 target, //distance + direction
        float unitsPerSecond,
        float fixedDeltaTime,
        bool returnToStart,
        string coordinateSpace,
        bool restrictTargetPosition,
        bool disableRendering,
        bool useLimits = false
    ) {
        //Debug.Log("starting moveArmTarget in ArticulatedArmController");
        float tolerance = 1e-3f;
        float maxTimePassed = 10.0f;
        int positionCacheSize = 10;

        float distance = Vector3.Distance(target, Vector3.zero);
        //Debug.Log($"raw distance value: {distance}");
        //calculate distance to move offset by the wristPlaceholderTransform local z value
        //add the -z offset each time to actually move the same "distance" as the IK arm
        distance = distance + wristPlaceholderForwardOffset;
        //Debug.Log($"actual distance to move: {distance}");

        int direction = 0;

        //this is sort of a wonky way to detect direction but it'll work for noooooow
        if (target.z < 0) {
            direction = -1;
        }
        if (target.z > 0) {
            direction = 1;
        }

        Dictionary<ArticulatedArmJointSolver, float> jointToArmDistanceRatios = new Dictionary<ArticulatedArmJointSolver, float>();

        ArmMoveParams amp = new ArmMoveParams {
            distance = distance,
            speed = unitsPerSecond,
            tolerance = tolerance,
            maxTimePassed = maxTimePassed,
            positionCacheSize = positionCacheSize,
            direction = direction,
            useLimits = useLimits,
            maxForce = 20f
        };

        prepAllTheThingsBeforeJointMoves(joints[1], amp);

        // //get the total distance each joint can move based on the upper limits
        // float totalExtendDistance = 0.0f;

        // //loop through all extending joints to get the total distance each joint can move
        // for (int i = 1; i <= 4; i++) {
        //     totalExtendDistance += GetDriveUpperLimit(joints[i]);
        // }

        // //loop through all extending joints and get the ratio of movement each joint is responsible for
        // for (int i = 1; i <= 4; i++) {
        //     ArticulatedArmJointSolver thisJoint = joints[i];
        //     jointToArmDistanceRatios.Add(thisJoint, GetDriveUpperLimit(thisJoint) / totalExtendDistance);
        // }

        // //set each joint to move its specific distance
        // foreach (ArticulatedArmJointSolver joint in jointToArmDistanceRatios.Keys) {

        // //assign each joint the distance it needs to move to have the entire arm
        // //this means the distance each joint moves may be slightly different due to proportion of movement this joint is responsible for
        // float myDistance = distance * jointToArmDistanceRatios[joint];
        // Debug.Log($"joint {joint.transform.name} is moving ({myDistance}) out of total distance ({distance})");
        // ArmMoveParams amp = new ArmMoveParams {
        //     distance = myDistance,
        //     speed = unitsPerSecond,
        //     tolerance = tolerance,
        //     maxTimePassed = maxTimePassed,
        //     positionCacheSize = positionCacheSize,
        //     direction = direction
        // };

        //     //assign movement params to this joint
        //     //joint.PrepToControlJointFromAction(amp);
        //     prepAllTheThingsBeforeJointMoves(joint, amp);
        // }

        //now need to do move call here I think
        IEnumerator moveCall = resetArmTargetPositionRotationAsLastStep(
                ContinuousMovement.moveAB(
                    movable: this,
                    controller: controller,
                    fixedDeltaTime: disableRendering ? fixedDeltaTime : Time.fixedDeltaTime
            )
        );

        // StartCoroutine(moveCall);
        if (disableRendering) {
            ContinuousMovement.unrollSimulatePhysics(
                moveCall,
                fixedDeltaTime
            );
        } else {
            StartCoroutine(
                moveCall
            );
        }
    }

    public float GetDriveUpperLimit(ArticulatedArmJointSolver joint, JointAxisType jointAxisType = JointAxisType.Extend) {
        float upperLimit = 0.0f;

        if (jointAxisType == JointAxisType.Extend) {
            //z drive
            upperLimit = joint.myAB.zDrive.upperLimit;
        }

        if (jointAxisType == JointAxisType.Lift) {
            //y drive
            upperLimit = joint.myAB.yDrive.upperLimit;
        }

        //no revolute limit because it revolves in a circle forever

        return upperLimit;
    }

    // private IEnumerator AreAllTheJointsBackToIdle(List<ArticulatedArmJointSolver> jointsThatAreMoving, PhysicsRemoteFPSAgentController controller) {
    //     bool hasEveryoneStoppedYet = false;

    //     //keep checking if things are all idle yet
    //     //all individual joints should have a max timeout so this won't hang infinitely (i hope)
    //     while (hasEveryoneStoppedYet == false) {
    //         yield return new WaitForFixedUpdate();

    //         foreach (ArticulatedArmJointSolver joint in jointsThatAreMoving) {
    //             if (joint.extendState == ArmExtendState.Idle) {
    //                 hasEveryoneStoppedYet = true;
    //             } else {
    //                 hasEveryoneStoppedYet = false;
    //             }
    //         }
    //     }

    //     //done!
    //     controller.actionFinished(true);
    //     yield return null;
    // }

    public void moveArmBase(
        ArticulatedAgentController controller,
        float distance,
        float unitsPerSecond,
        float fixedDeltaTime,
        bool returnToStartPositionIfFailed,
        bool disableRendering,
        bool normalizedY,
        bool useLimits
    ) {
        Debug.Log("starting moveArmBase in ArticulatedArmController");
        float tolerance = 1e-3f;
        float maxTimePassed = 10.0f;
        int positionCacheSize = 10;

        int direction = 0;
        if (distance < 0) {
            direction = -1;
        }
        if (distance > 0) {
            direction = 1;
        }

        ArmMoveParams amp = new ArmMoveParams {
            distance = Mathf.Abs(distance),
            speed = unitsPerSecond,
            tolerance = tolerance,
            maxTimePassed = maxTimePassed,
            positionCacheSize = positionCacheSize,
            direction = direction,
            useLimits = useLimits,
            maxForce = 20f
        };

        ArticulatedArmJointSolver liftJoint = joints[0];
        //preset the joint's movement parameters ahead of time
        prepAllTheThingsBeforeJointMoves(liftJoint, amp);
        //liftJoint.PrepToControlJointFromAction(amp);

        //Vector3 target = new Vector3(this.transform.position.x, distance, this.transform.position.z);

        //now need to do move call here I think
        IEnumerator moveCall = resetArmTargetPositionRotationAsLastStep(
                ContinuousMovement.moveAB(
                    movable: this,
                    controller: controller,
                    fixedDeltaTime: disableRendering ? fixedDeltaTime : Time.fixedDeltaTime
            )
        );

        if (disableRendering) {
            ContinuousMovement.unrollSimulatePhysics(
                moveCall,
                fixedDeltaTime
            );
        } else {
            StartCoroutine(
                moveCall
            );
        }
    }

    public void moveArmBaseUp(
        ArticulatedAgentController controller,
        float distance,
        float unitsPerSecond,
        float fixedDeltaTime,
        bool returnToStartPositionIfFailed,
        bool disableRendering,
        bool useLimits
    ) {
        moveArmBase(
            controller: controller,
            distance: distance,
            unitsPerSecond: unitsPerSecond,
            fixedDeltaTime: fixedDeltaTime,
            returnToStartPositionIfFailed: returnToStartPositionIfFailed,
            disableRendering: disableRendering,
            normalizedY: false,
            useLimits: useLimits
        );

    }

    private void prepAllTheThingsBeforeJointMoves(ArticulatedArmJointSolver joint, ArmMoveParams armMoveParams) {
        //FloorCollider.material = sticky;
        joint.PrepToControlJointFromAction(armMoveParams);
    }

    public void rotateWrist(
        ArticulatedAgentController controller,
        float distance,
        float degreesPerSecond,
        bool disableRendering,
        float fixedDeltaTime,
        bool returnToStartPositionIfFailed
    ) {
        Debug.Log("starting rotateWrist in ArticulatedArmController");
        float tolerance = 1e-3f;
        float maxTimePassed = 10.0f;
        int positionCacheSize = 10;

        int direction = 0;
        if (distance < 0) {
            direction = -1;
        }
        if (distance > 0) {
            direction = 1;
        }

        ArmMoveParams amp = new ArmMoveParams {
            distance = Mathf.Abs(distance),
            speed = degreesPerSecond,
            tolerance = tolerance,
            maxTimePassed = maxTimePassed,
            positionCacheSize = positionCacheSize,
            direction = direction,
            maxForce = 20f
        };

        ArticulatedArmJointSolver wristJoint = joints[2];
        //preset the joint's movement parameters ahead of time
        prepAllTheThingsBeforeJointMoves(wristJoint, amp);

        //now need to do move call here I think
        IEnumerator moveCall = resetArmTargetPositionRotationAsLastStep(
                ContinuousMovement.moveAB(
                    movable: this,
                    controller: controller,
                    fixedDeltaTime: disableRendering ? fixedDeltaTime : Time.fixedDeltaTime
            )
        );

        if (disableRendering) {
            ContinuousMovement.unrollSimulatePhysics(
                moveCall,
                fixedDeltaTime
            );
        } else {
            StartCoroutine(
                moveCall
            );
        }
    }

    public override bool PickupObject(List<string> objectIds, ref string errorMessage) {
        Debug.Log("calling PickupObject from ArticulatedArmController");
        bool pickedUp = false;

        foreach (SimObjPhysics sop in WhatObjectsAreInsideMagnetSphereAsSOP(onlyPickupable: true)) {
            Debug.Log($"sop named: {sop.objectID} found inside sphere");
            if (objectIds != null) {
                //only grab objects specified by objectIds
                if (!objectIds.Contains(sop.objectID)) {
                    continue;
                }
            }

            sop.BeingPickedUpByArticulatedAgent(this);

            // Rigidbody rb = sop.GetComponent<Rigidbody>();

            // //make sure rigidbody of object is not kinematic
            // rb.isKinematic = false;

            // //add a fixed joint to this picked up object
            // FixedJoint ultraHand = sop.transform.gameObject.AddComponent<FixedJoint>();
            // //add reference to the wrist joint as connected articulated body 
            // ultraHand.connectedArticulationBody = FinalJoint.GetComponent<ArticulationBody>();
            // ultraHand.enableCollision = true;
            //add to heldObjects list so we know when to drop

            pickedUp = true;
            heldObjects.Add(sop);
        }

        if (!pickedUp) {
            errorMessage = (
                objectIds != null
                ? "No objects (specified by objectId) were valid to be picked up by the arm"
                : "No objects were valid to be picked up by the arm"
            );
        }

        return pickedUp;
    }

    //called by ArmAgentController ReleaseObject
    public override void DropObject() { 
        foreach (SimObjPhysics sop in heldObjects)
        {
            //remove the joint component
            //may need a null check for if we decide to break joints via force at some poine.
            //look into the OnJointBreak callback if needed
            Destroy(sop.transform.GetComponent<FixedJoint>());

            sop.BeingDropped();
        }
        
        heldObjects.Clear();
    }

    protected override void resetArmTarget() {



        foreach (ArticulatedArmJointSolver joint in joints) {
            ArticulationBody myAB = joint.myAB;

            if (myAB == null) {
                Debug.LogWarning("Articulated body is null, skipping.");
                continue;
            }

            // Check the joint type and get the current joint position and velocity
            if (myAB.jointType == ArticulationJointType.PrismaticJoint) {

//                Debug.Log($"joint {joint.gameObject}");
//                Debug.Log($"joint {myAB.jointType}");
//                Debug.Log($"solverIterations {myAB.solverIterations}");
//                Debug.Log($"solverVelocityIterations {myAB.solverVelocityIterations}");

                if (myAB.dofCount != 1) {
                    throw new NotImplementedException("Prismatic joint must have 1 degree of freedom");
                }
                float currentPosition = myAB.jointPosition[0];

                ArticulationDrive xDrive = myAB.xDrive;
                ArticulationDrive yDrive = myAB.yDrive;
                ArticulationDrive zDrive = myAB.zDrive;

                // Super hacky way to get which drive is active
                string whichDrive = "x";
                ArticulationDrive activeDrive = xDrive;
                if (yDrive.target != 0.0f) {
                    activeDrive = yDrive;
                    whichDrive = "y";
                }
                if (zDrive.target != 0.0f) {
                    activeDrive = zDrive;
                    whichDrive = "z";
                }

                Debug.Log(currentPosition);
                Debug.Log(whichDrive);

                activeDrive.target = currentPosition;
                activeDrive.targetVelocity = 0f;

                if (whichDrive == "x") {
                    myAB.xDrive = activeDrive;
                }
                if (whichDrive == "y") {
                    myAB.yDrive = activeDrive;
                }
                if (whichDrive == "z") {
                    myAB.zDrive = activeDrive;
                }
            }
            else if (myAB.jointType == ArticulationJointType.RevoluteJoint) {
                // For revolute joints
                if (myAB.dofCount != 1) {
                    throw new NotImplementedException("Revolute joint must have 1 degree of freedom");
                }
                float currentPosition = Mathf.Rad2Deg * myAB.jointPosition[0]; // Weirdly not in degrees

                // TODO: We just assume that the joint is on the x axis, we don't have a good way to check
                //       for otherwise atm.
                ArticulationDrive xDrive = myAB.xDrive;

                xDrive.target = currentPosition;
                xDrive.targetVelocity = 0f;

                myAB.xDrive = xDrive;
            } else {
                throw new NotImplementedException($"Unsupported joint type {myAB.jointType}");
            }
        }
    }

    //ignore this, we need new metadata that makes more sense for the articulation heirarchy soooooo
    public override ArmMetadata GenerateMetadata() {
        // TODO: Reimplement, low prio for benchmark
        ArmMetadata meta = new ArmMetadata();
        return meta;
    }

    //actual metadata implementation for articulation heirarchy 
    public ArticulationArmMetadata GenerateArticulationMetadata() {
        ArticulationArmMetadata meta = new ArticulationArmMetadata();

        List<ArticulationJointMetadata> metaJoints = new List<ArticulationJointMetadata>();

        //declaring some stuff for processing metadata
        Quaternion currentRotation;
        float angleRot;
        Vector3 vectorRot;

        for (int i = 0; i < joints.Count(); i++) {
            ArticulationJointMetadata jMeta = new ArticulationJointMetadata();

            jMeta.name = joints[i].name;

            jMeta.position = joints[i].transform.position;

            jMeta.rootRelativePosition = joints[0].transform.InverseTransformPoint(joints[i].transform.position);

            jMeta.jointHeirarchyPosition = i;

            // WORLD RELATIVE ROTATION
            currentRotation = joints[i].transform.rotation;

            // Check that world-relative rotation is angle-axis-notation-compatible
            if (currentRotation != new Quaternion(0, 0, 0, -1)) {
                currentRotation.ToAngleAxis(angle: out angleRot, axis: out vectorRot);
                jMeta.rotation = new Vector4(vectorRot.x, vectorRot.y, vectorRot.z, angleRot);
            } else {
                jMeta.rotation = new Vector4(1, 0, 0, 0);
            }

            // ROOT-JOINT RELATIVE ROTATION
            // Grab rotation of current joint's angler relative to root joint
            currentRotation = Quaternion.Inverse(joints[0].transform.rotation) * joints[i].transform.rotation;
            if (currentRotation != new Quaternion(0, 0, 0, -1)) {
                currentRotation.ToAngleAxis(angle: out angleRot, axis: out vectorRot);
                jMeta.rootRelativeRotation = new Vector4(vectorRot.x, vectorRot.y, vectorRot.z, angleRot);
            } else {
                jMeta.rootRelativeRotation = new Vector4(1, 0, 0, 0);
            }

            // LOCAL POSITION AND LOCAL ROTATION
            //get local position and local rotation relative to immediate parent in heirarchy
            if (i != 0) {
                jMeta.localPosition = joints[i - 1].transform.InverseTransformPoint(joints[i].transform.position);
                
                var currentLocalRotation = Quaternion.Inverse(joints [i - 1].transform.rotation) * joints[i].transform.rotation;
                if(currentLocalRotation != new Quaternion(0, 0, 0, -1)) {
                    currentLocalRotation.ToAngleAxis(angle: out angleRot, axis: out vectorRot);
                    jMeta.localRotation = new Vector4(vectorRot.x, vectorRot.y, vectorRot.z, angleRot);
                } else {
                jMeta.localRotation = new Vector4(1, 0, 0, 0);
                }
            } else {
                //special case for the lift since its the base of the arm
                jMeta.localPosition = jMeta.position;
                jMeta.localRotation = jMeta.rootRelativeRotation;
            }

            metaJoints.Add(jMeta);
        }

        meta.joints = metaJoints.ToArray();

        // metadata for any objects currently held by the hand on the arm
        // note this is different from objects intersecting the hand's sphere,
        // there could be a case where an object is inside the sphere but not picked up by the hand
        List<string> heldObjectIDs = new List<string>();
        if (heldObjects != null) {
            foreach (SimObjPhysics sop in heldObjects) {
                heldObjectIDs.Add(sop.objectID);
            }
        }

        meta.heldObjects = heldObjectIDs;
        meta.handSphereCenter = magnetSphere.transform.TransformPoint(magnetSphere.center);
        meta.handSphereRadius = magnetSphere.radius;
        List<SimObjPhysics> objectsInMagnet = WhatObjectsAreInsideMagnetSphereAsSOP(false);
        meta.pickupableObjects = objectsInMagnet.Where(
            x => x.PrimaryProperty == SimObjPrimaryProperty.CanPickup
        ).Select(x => x.ObjectID).ToList();
        meta.objectsInsideHandSphereRadius = objectsInMagnet.Select(x => x.ObjectID).ToList();

        return meta;
    }

#if UNITY_EDITOR
    public class GizmoDrawCapsule {
        public Vector3 p0;
        public Vector3 p1;
        public float radius;
    }

    List<GizmoDrawCapsule> debugCapsules = new List<GizmoDrawCapsule>();

    private void OnDrawGizmos() {
        if (debugCapsules.Count > 0) {
            foreach (GizmoDrawCapsule thing in debugCapsules) {
                Gizmos.DrawWireSphere(thing.p0, thing.radius);
                Gizmos.DrawWireSphere(thing.p1, thing.radius);
            }
        }
    }
#endif
}
