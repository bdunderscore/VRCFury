using System.Collections.Generic;
using System.Linq;
using VF.Builder;
using VF.Builder.Exceptions;
using VF.Feature.Base;
using VF.Utils;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using AnimatorStateExtensions = VF.Builder.AnimatorStateExtensions;

namespace VF.Feature {
    /**
     * This builder is responsible for checking if you have multiple owners for controllers which should only come from one
     * source, such as locomotion or TPose.
     */
    public class ControllerConflictBuilder : FeatureBuilder {
        [FeatureBuilderAction(FeatureOrder.ControllerConflictCheck)]
        public void Apply() {
            var singleOwnerTypes = new HashSet<VRCAvatarDescriptor.AnimLayerType>() {
                VRCAvatarDescriptor.AnimLayerType.Base,
                VRCAvatarDescriptor.AnimLayerType.TPose,
                VRCAvatarDescriptor.AnimLayerType.IKPose,
                VRCAvatarDescriptor.AnimLayerType.Sitting
            };

            foreach (var controller in manager.GetAllUsedControllers()) {
                var type = controller.GetType();
                var uniqueOwners = new HashSet<string>();
                foreach (var layer in controller.GetLayers()) {
                    // Ignore empty layers (bask mask, junk layers, etc)
                    if (layer.stateMachine.defaultState == null) continue;
                    uniqueOwners.Add(controller.GetLayerOwner(layer));
                }

                if (uniqueOwners.Count > 1 && singleOwnerTypes.Contains(type)) {
                    throw new VRCFBuilderException(
                        "Your avatar contains multiple implementations for a base playable layer." +
                        " Usually, this means you are trying to add GogoLoco, but your avatar already has a Base controller." +
                        " The fix is usually to remove the custom Base controller that came with your avatar on the VRC Avatar Descriptor.\n\n" +
                        "Layer type: " + VRCFEnumUtils.GetName(type) + "\n" +
                        "Sources:\n" + string.Join("\n", uniqueOwners)
                    );
                }
            }
        }
    }
}
