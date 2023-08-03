using System;
using System.Collections.Generic;
using UnityEngine;
using VF.Builder;
using VF.Feature.Base;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace VF.Feature {
    public class VRCBehaviourRewriterBuilder : FeatureBuilder {
        // TODO: Deal with conflicts when multiple owners:
        // * turn on/off locomotion
        // * turn on/off tracking
        // * turn on/off pose space

        [FeatureBuilderAction(FeatureOrder.VRCBehaviourRewrite)]
        public void Apply() {
            foreach (var controller in manager.GetAllUsedControllers()) {
                foreach (var layer in controller.GetLayers()) {
                    foreach (var behaviour in new AnimatorIterator.Behaviours().From(controller.GetRaw())) {
                        CollectOwners(behaviour, controller.GetLayerOwner(layer));
                    }
                }
            }
        }
        
        private List<(OnOffType, string)> owners = new List<(OnOffType, string)>();

        private void CollectOwners(StateMachineBehaviour behaviour, string owner) {
            if (behaviour is VRCAnimatorTrackingControl trackingControl) {
                foreach (var type in VRCFEnumUtils.GetValues<OnOffType>()) {
                    if (GetTracking(trackingControl, type) != VRC_AnimatorTrackingControl.TrackingType.NoChange) {
                        owners.Add((type, owner));
                    }
                }
            }

            if (behaviour is VRCAnimatorLocomotionControl locomotionControl) {
                owners.Add((OnOffType.Locomotion, owner));
            }

            if (behaviour is VRCAnimatorTemporaryPoseSpace poseSpace) {
                owners.Add((OnOffType.PoseSpace, owner));
            }
        }

        delegate void WithTrackingCb(ref VRC_AnimatorTrackingControl.TrackingType field);
        private void WithTracking(VRCAnimatorTrackingControl control, OnOffType type, WithTrackingCb with) {
            switch (type) {
                case OnOffType.TrackingHead: with(ref control.trackingHead); break;
                case OnOffType.TrackingLeftHand: with(ref control.trackingLeftHand); break;
                case OnOffType.TrackingRightHand: with(ref control.trackingRightHand); break;
                case OnOffType.TrackingHip: with(ref control.trackingHip); break;
                case OnOffType.TrackingLeftFoot: with(ref control.trackingLeftFoot); break;
                case OnOffType.TrackingRightFoot: with(ref control.trackingRightFoot); break;
                case OnOffType.TrackingLeftFingers: with(ref control.trackingLeftFingers); break;
                case OnOffType.TrackingRightFingers: with(ref control.trackingRightFingers); break;
                case OnOffType.TrackingEyes: with(ref control.trackingEyes); break;
                case OnOffType.TrackingMouth: with(ref control.trackingMouth); break;
            }
        }
        private VRC_AnimatorTrackingControl.TrackingType GetTracking(VRCAnimatorTrackingControl control, OnOffType type) {
            VRC_AnimatorTrackingControl.TrackingType value = VRC_AnimatorTrackingControl.TrackingType.NoChange;
            WithTracking(control, type, (ref VRC_AnimatorTrackingControl.TrackingType v) => value = v);
            return value;
        }
        private void SetTracking(VRCAnimatorTrackingControl control, OnOffType type, VRC_AnimatorTrackingControl.TrackingType value) {
            WithTracking(control, type, (ref VRC_AnimatorTrackingControl.TrackingType v) => v = value);
        }

        private enum OnOffType {
            Locomotion,
            PoseSpace,
            TrackingHead,
            TrackingLeftHand,
            TrackingRightHand,
            TrackingHip,
            TrackingLeftFoot,
            TrackingRightFoot,
            TrackingLeftFingers,
            TrackingRightFingers,
            TrackingEyes,
            TrackingMouth,
        }

        private List<(object, VFABool)> Collect() {
            foreach (var controller in manager.GetAllUsedControllers()) {
                foreach (var state in new AnimatorIterator.States().From(controller.GetRaw())) {
                    
                }
            }

            return null;
        }
    }
}