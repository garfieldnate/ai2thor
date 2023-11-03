"""
Classes defined for planning grasping that is specific to Stretch RE1 robot

To install open3d
 !python -m pip install open3d

To install detectron2
 !python -m pip install 'git+https://github.com/facebookresearch/detectron2.git'

To install OwlVit
 ! pip install transformers

To install Fast SAM
  !pip install git+https://github.com/CASIA-IVA-Lab/FastSAM.git
  !pip install git+https://github.com/openai/CLIP.git
  !pip install ultralytics==8.0.120

  To download model weights
   mkdir model_chekpoints && cd model_checkpoints
   !wget https://huggingface.co/spaces/An-619/FastSAM/resolve/main/weights/FastSAM.pt 
   
"""

import os
import cv2 
import open3d
import json
import numpy as np
from scipy.spatial.transform import Rotation as R
import math
import torch


## CONSTANTS TRANSFORMATION MATRIX
T_ARM_FROM_BASE_188 = np.array([[   -0.99929,   -0.021817,    0.030712,   -0.062167],
       [  -0.037528,     0.50515,    -0.86222,   -0.047745],
       [   0.003297,    -0.86276,    -0.50561,      1.4732],
       [          0,           0,           0,           1]])

T_ARM_FROM_BASE_205 = np.array([[   -0.99652,   -0.080247,   -0.022519,   -0.055535],
       [  -0.023487,     0.52961,    -0.84792,   -0.053421],
       [   0.079969,    -0.84444,    -0.52965,      1.4676],
       [          0,           0,           0,           1]])

T_ROTATED_STRETCH_FROM_BASE = np.array([[-0.00069263, 1, -0.0012349, -0.017],
                    [ 0.5214, -0.00069263, -0.85331, -0.038],
                    [ -0.85331, -0.0012349, -0.52139, 1.294],
                    [ 0, 0, 0, 1]])

## CONSTANT CAMERA INTRINSIC 
STRETCH_INTR = {'coeffs': [0.0, 0.0, 0.0, 0.0, 0.0], 'fx': 911.8329467773438, 'fy': 911.9554443359375, 'height': 720, 'ppx': 647.63037109375, 'ppy': 368.0513000488281, 'width': 1280, 'depth_scale': 0.0010000000474974513}
ARM_INTR = {'coeffs': [-0.05686680227518082, 0.06842068582773209, -0.0004524677060544491, 0.0006787769380025566, -0.022475285455584526], 'fx': 640.1092529296875, 'fy': 639.4522094726562, 'height': 720, 'ppx': 652.3712158203125, 'ppy': 368.69549560546875, 'width': 1280, 'depth_scale': 0.0010000000474974513}


class BaseObjectDetector():
    def __init__(self, camera_source="arm205"):
        self.camera_source = camera_source
        self.update_camera_info(camera_source)

    #def predict_instance_segmentation(self, rgb):
    #    raise NotImplementedError

    def update_camera_info(self, camera_source):
        ## TODO: read from config
        if camera_source == "stretch":
            #with open(os.path.join(os.path.dirname(__file__),'camera_intrinsics_102422073668.txt')) as f:
            #    intr = json.load(f)
            intr = STRETCH_INTR
            self.intrinsic = open3d.camera.PinholeCameraIntrinsic(intr["width"],intr["height"],intr["fx"],intr["fy"],intr["ppx"],intr["ppy"])    
            self.depth_scale = intr["depth_scale"]
            self.CameraPose = T_ROTATED_STRETCH_FROM_BASE
        elif camera_source == "arm205":
            intr = ARM_INTR
            self.intrinsic = open3d.camera.PinholeCameraIntrinsic(intr["width"],intr["height"],intr["fx"],intr["fy"],intr["ppx"],intr["ppy"])    
            self.depth_scale = intr["depth_scale"]
            self.CameraPose = T_ARM_FROM_BASE_205
        elif camera_source == "arm188":
            intr = ARM_INTR
            self.intrinsic = open3d.camera.PinholeCameraIntrinsic(intr["width"],intr["height"],intr["fx"],intr["fy"],intr["ppx"],intr["ppy"])    
            self.depth_scale = intr["depth_scale"]
            self.CameraPose = T_ARM_FROM_BASE_188        
        else:
            print("Camera source can only be [arm205, arm188 or stretch]")
            print("Please call 'update_camera_info(camera_source)' with the right camera source")

    def get_target_mask(self, object_str, rgb):
        raise NotImplementedError

    def get_target_object_pose(self, rgb, depth, mask):
        if mask is None:
            exit()

        rgb = np.array(rgb.copy())
        rgbim = open3d.geometry.Image(rgb.astype(np.uint8))

        depth[mask==False] = -0.1
        depth = np.asarray(depth).astype(np.float32) / self.depth_scale
        depthim = open3d.geometry.Image(depth)

        rgbd = open3d.geometry.RGBDImage.create_from_color_and_depth(rgbim, depthim, convert_rgb_to_intensity=False)
        pcd = open3d.geometry.PointCloud.create_from_rgbd_image(rgbd, self.intrinsic)
        pcd.remove_statistical_outlier(nb_neighbors=20, std_ratio=2.0)

        center = pcd.get_center()
        bbox = pcd.get_oriented_bounding_box()
        
        ##TODO: if len(pcd.points) is zero, there is a problem with a depth image)
        center[2] = bbox.center[2] #TODO. investigate. cetner seems to be little above where bbox.center seems middle
        Randt=np.concatenate((bbox.R, np.expand_dims(center, axis=1)),axis=1) # pitfall: arrays need to be passed as a tuple
        #Randt=np.concatenate((bbox.R, np.expand_dims(bbox.center, axis=1)),axis=1) # pitfall: arrays need to be passed as a tuple
        lastrow=np.expand_dims(np.array([0,0,0,1]),axis=0)
        objectPoseCamera = np.concatenate((Randt,lastrow)) 

        return self.CameraPose @ objectPoseCamera



class OwlVitSegAnyObjectDetector(BaseObjectDetector):
    """
    https://huggingface.co/docs/transformers/model_doc/owlvit#transformers.OwlViTForObjectDetection
    """

    def __init__(self, fastsam_path, camera_source="arm205", device="cpu"):
        super().__init__(camera_source)
        self.device = device

        # initialize OwlVit
        from transformers import OwlViTProcessor, OwlViTForObjectDetection
        self.processor_owlvit = OwlViTProcessor.from_pretrained("google/owlvit-base-patch32")
        self.model_owlvit = OwlViTForObjectDetection.from_pretrained("google/owlvit-base-patch32")
        self.model_owlvit.eval()

        # initialize SegAnything
        from fastsam import FastSAM #, FastSAMPrompt 
        self.model_fastsam = FastSAM(fastsam_path) #os.path.join(os.path.dirname(__file__),'model_checkpoints/FastSAM.pt'))
        self.model_fastsam.to(device=device)

    def get_target_mask(self, object_str, rgb):
        if self.camera_source == "stretch":
            rgb = cv2.rotate(rgb, cv2.ROTATE_90_CLOCKWISE) # it works better for stretch cam

        def predict_object_detection(rgb, object_str):
            with torch.no_grad():
                inputs = self.processor_owlvit(text=object_str, images=rgb, return_tensors="pt")
                outputs = self.model_owlvit(**inputs)

            target_sizes = torch.Tensor([rgb.shape[0:2]])
            results = self.processor_owlvit.post_process_object_detection(outputs=outputs, target_sizes=target_sizes, threshold=0.1)
            boxes = results[0]["boxes"].detach().cpu().numpy() #results[0]["scores"], results[0]["labels"]
            scores = results[0]["scores"].detach().cpu().numpy()

            if len(boxes)==0:
                print(f"{object_str} Not Detected.")
                return None
            
            ind = np.argmin(scores)
            return [round(i) for i in boxes[ind].tolist()] #xyxy

        bbox = predict_object_detection(rgb=rgb, object_str=object_str)

        everything_results = self.model_fastsam(
                rgb,
                device=self.device,
                retina_masks=True,
                imgsz=1024, #1024 is default 
                conf=0.4,
                iou=0.9    
                )

        from fastsam import FastSAMPrompt 
        prompt_process = FastSAMPrompt(rgb, everything_results, device=self.device)
        masks = prompt_process.box_prompt(bboxes=[bbox])
        
        if self.camera_source == "stretch":
            # rotate back
            mask = cv2.rotate(np.array(masks[0]), cv2.ROTATE_90_COUNTERCLOCKWISE)
        else:
            mask = np.array(masks[0])
        return mask


    def get_target_object_pose(self, rgb, depth, mask):
        return super().get_target_object_pose(rgb, depth, mask)


class DoorKnobDetector(OwlVitSegAnyObjectDetector):
    def __init__(self, fastsam_path, camera_source="arm205", device="cpu"):
        super().__init__(fastsam_path, camera_source)

    def get_target_mask(self, rgb, object_str="a photo of a doorknob"):
        return super().get_target_mask(object_str=object_str, rgb=rgb)

    def get_door_pointcloud(self, rgb, depth):
        mask = self.get_target_mask(rgb, object_str="a photo of a door")

        _rgb = np.array(rgb.copy())
        rgbim = open3d.geometry.Image(_rgb.astype(np.uint8))

        _depth = depth.copy()
        _depth[mask==False] = -0.1
        _depth = np.asarray(_depth).astype(np.float32) / self.depth_scale
        depthim = open3d.geometry.Image(_depth)

        rgbd = open3d.geometry.RGBDImage.create_from_color_and_depth(rgbim, depthim, convert_rgb_to_intensity=False)
        pcd = open3d.geometry.PointCloud.create_from_rgbd_image(rgbd, self.intrinsic)
        #pcd_downsampled = pcd.voxel_down_sample(voxel_size=0.05)

        return pcd 

    def get_target_object_pointcloud(self, rgb, depth, mask):
        if mask is None:
            exit()

        _rgb = np.array(rgb.copy())
        rgbim = open3d.geometry.Image(_rgb.astype(np.uint8))

        _depth = depth.copy()
        _depth[mask==False] = -0.1
        _depth = np.asarray(_depth).astype(np.float32) / self.depth_scale
        depthim = open3d.geometry.Image(_depth)

        rgbd = open3d.geometry.RGBDImage.create_from_color_and_depth(rgbim, depthim, convert_rgb_to_intensity=False)
        pcd = open3d.geometry.PointCloud.create_from_rgbd_image(rgbd, self.intrinsic)
        return pcd 

    def get_target_object_pose(self, rgb, depth, mask, distance_m=0.205):
        normval_vector = self.get_center_normal_vector(self.get_door_pointcloud(rgb, depth))

        pcd = self.get_target_object_pointcloud(rgb, depth, mask)
        center = pcd.get_center()
        bbox = pcd.get_oriented_bounding_box()
        ##TODO: if len(pcd.points) is zero, there is a problem with a depth image)

        Randt=np.concatenate((bbox.R, np.expand_dims(bbox.center, axis=1)),axis=1) # pitfall: arrays need to be passed as a tuple
        lastrow=np.expand_dims(np.array([0,0,0,1]),axis=0)
        objectPoseCamera = np.concatenate((Randt,lastrow)) 

        preplan_pose = self.plan_pregrasp_pose(objectPoseCamera, normval_vector, distance_m)
        return [self.CameraPose @ objectPoseCamera, self.CameraPose @ preplan_pose]

    def plan_pregrasp_pose(self, object_pose, normal_vector, distance_m=0.205):
        # GraspCenter to Arm Offset = 0.205
        # Compute the waypoint pose
        waypoint_pose = object_pose.copy()
        translation_offset = distance_m * normal_vector
        waypoint_pose[0:3, 3] += translation_offset
        return waypoint_pose
    
    def get_center_normal_vector(self, pcd):
        # Downsample the point cloud to speed up the normal estimation
        #pcd = pcd.voxel_down_sample(voxel_size=0.05)
        
        # Estimate normals
        pcd.estimate_normals() #search_param=open3d.geometry.KDTreeSearchParamHybrid(radius=0.1, max_nn=30))
        pcd.normalize_normals()

        # Get the center point of the point cloud
        #center_point = np.asarray(pcd.points).mean(axis=0)
        center_point = pcd.get_center()

        # Find the index of the nearest point to the center
        pcd_tree = open3d.geometry.KDTreeFlann(pcd)
        k, idx, _ = pcd_tree.search_knn_vector_3d(center_point, 1)

        # Get the normal at the center point
        return np.asarray(pcd.normals)[idx[0]]


class ObjectDetector(BaseObjectDetector):
    def __init__(self, camera_source="arm205", device="cpu"):    
        super().__init__(camera_source)
        
        # import
        import detectron2
        from detectron2 import model_zoo
        from detectron2.engine import DefaultPredictor
        from detectron2.config import get_cfg

        from ai2thor.coco_wordnet import synset_to_ms_coco

        ## DEFAULT INSTANCE SEGMENTATION
        cfg = get_cfg()
        # add project-specific config (e.g., TensorMask) here if you're not running a model in detectron2's core library
        cfg.merge_from_file(model_zoo.get_config_file("COCO-InstanceSegmentation/mask_rcnn_R_50_FPN_3x.yaml"))
        cfg.MODEL.ROI_HEADS.SCORE_THRESH_TEST = 0.5  # set threshold for this model
        # Find a model from detectron2's model zoo. You can use the https://dl.fbaipublicfiles... url as well
        cfg.MODEL.WEIGHTS = model_zoo.get_checkpoint_url("COCO-InstanceSegmentation/mask_rcnn_R_50_FPN_3x.yaml")
        cfg.MODEL.DEVICE = device  #cpu or mps or cuda

        self.predictor = DefaultPredictor(cfg)
        self.predictor_cls_map = synset_to_ms_coco


    def predict_instance_segmentation(self, rgb):
        #rgb = cv2.cvtColor(rgb, cv2.COLOR_BGR2RGB)
        if self.camera_source == "stretch":
            rgb = cv2.rotate(rgb, cv2.ROTATE_90_CLOCKWISE) # it works better for stretch cam
        outputs = self.predictor(rgb)

        predict_classes = outputs["instances"].pred_classes.to("cpu").numpy()
        predict_masks = outputs["instances"].pred_masks.to("cpu").numpy()

        masks = []
        for mask in predict_masks:
            mask = mask*1.0

            if self.camera_source == "stretch":
                # rotate counter clockwise
                mask = cv2.rotate(np.array(mask), cv2.ROTATE_90_COUNTERCLOCKWISE)

            masks.append(mask)
        predict_masks = np.array(masks)

        return predict_classes, predict_masks
    

    def get_target_mask(self, object_str, rgb):
        # get target object id
        id = self.predictor_cls_map[object_str]

        cls, masks = self.predict_instance_segmentation(rgb)

        # check if target object is detected
        if id in cls:
            return masks[np.where(cls == id)[0]][0] #(1, w, h)-> (w,h)
        else:
            print("Target object " + object_str + " not detected.")
            return None
        

    def get_target_object_pose(self, rgb, depth, mask):
        return super().get_target_object_pose(rgb, depth, mask)




class GraspPlanner():
    """ Naive grasp Planner """
    def __init__(self):
        pass
    
    def plan_lift_extenion(self, object_position, curr_lift_position):
        return object_position[2] + 0.168 - (curr_lift_position-0.21) - 0.41 #meters

    def plan_arm_extension(self, object_position, curr_arm_extension_position):
        return -1*object_position[1] - 0.205 - 0.254 - curr_arm_extension_position + 0.083 # -0.1 + 0.115 

    def plan_base_rotation(self, object_position):
        return -1*np.degrees(np.arctan2(object_position[1], object_position[0])) # bc stretch moves clockwise

    def plan_grasp_trajectory(self, object_position, last_event):
        ## TODO: should they be separated? and not command all at once?
        ## TODO: is object position whihtn a reachable range? Add an error margin as an argument to determine.
        
        trajectory = []
        # open grasper 
        trajectory.append({"action": "MoveGrasp", "args": {"move_scalar":100}})
        
        
        # lift1
        trajectory.append({"action": "MoveArmBase", "args": {"move_scalar": self.plan_lift_extenion(object_position, last_event.metadata["arm"]["lift_m"])}})
        
        # rotate base
        trajectory.append({"action": "RotateAgent", "args": {"move_scalar": self.plan_base_rotation(object_position) - 90}})
        
        # extend arm
        trajectory.append({"action": "MoveArmExtension", "args": {"move_scalar": self.plan_arm_extension(object_position, last_event.metadata["arm"]["extension_m"])}})
        
        # TODO: wrist will be out. fix the amount of rotation and sign
        # rotate wrist out
        #if np.degrees(last_event.metadata["arm"]["wrist_degrees"]) != 0.0:
        #    trajectory.append({"action": "MoveWrist", "args": {"move_scalar":  180 + np.degrees(last_event.metadata["arm"]["wrist_degrees"])%180 }})
        trajectory.append({"action": "WristTo", "args": {"move_to":  0}})

        # close grapser
        #trajectory.append({"action": "MoveGrasp", "args": {"move_scalar":-100}})

        return {"action": trajectory}

    

class DoorKnobGraspPlanner(GraspPlanner):
    def isReaable(self):
        ## Not reachable when
        #1. grasper center <-> object is beyond a threashold
        #2. needs to move the mobiel base 
        pass 

    def plan_grasp_trajectory(self, object_waypoints, last_event):
        object_pose, pregrasp_pose = object_waypoints
        
        object_position = object_pose[0:3,3]
        pregrasp_position = pregrasp_pose[0:3,3]

        ## FIRST ACTION
        # 1. move a wrist to a pregrasp position
        # 2. rotate wrist to align with the object center
        trajectory = []

        # open grasper 
        trajectory.append({"action": "MoveGrasp", "args": {"move_scalar":100}})
        
        # lift
        lift_offset = 0.1
        trajectory.append({"action": "MoveArmBase", "args": {"move_scalar": lift_offset + self.plan_lift_extenion(pregrasp_position, last_event.metadata["arm"]["lift_m"])}})
        
        # rotate base
        # TODO: omit rotate. and if not reachable call it failure
        self.plan_base_rotation(pregrasp_position)# - 90
        self.plan_base_rotation(object_position)# - 90

        trajectory.append({"action": "RotateAgent", "args": {"move_scalar": self.plan_base_rotation(pregrasp_position) - 90}})
        
        # extend arm
        arm_offset = 0.205
        trajectory.append({"action": "MoveArmExtension", "args": {"move_scalar": arm_offset + self.plan_arm_extension(pregrasp_position, last_event.metadata["arm"]["extension_m"])}})

        # rotate wrist - stretch wrist moves clockwise
        # pregrasp position 's -Y direction is X 
        # pregrasp position 's -X direction is Y 
        wrist_to_joint_offset=0.0 #0.05
        x_delta, y_delta = (object_position - pregrasp_position)[0:2]
        wrist_offset = np.degrees(np.arctan2(-x_delta-wrist_to_joint_offset, -y_delta)) # arctan2(y,x)
        trajectory.append({"action": "WristTo", "args": {"move_to":  wrist_offset}})

        first_actions = {"action": trajectory}


        ## SECOND ACTION
        # 1. move arm base down a predetermined to object center
        # 2. grasp
        second_actions = {"action": [
          {"action": "MoveArmBase", "args": {"move_scalar": -lift_offset}}  
        ]}
        
        # close grapser
        #trajectory.append({"action": "MoveGrasp", "args": {"move_scalar":-100}})

        return [first_actions, second_actions]



class VIDAGraspPlanner(GraspPlanner):
    def __init__(self):
        super().__init__()
        self.wrist_yaw_from_base = -0.020 # -0.025 # FIXED - should be.
        self.arm_offset = 0.140
        self.lift_base_offset = 0.192 #base to lift
        self.lift_wrist_offset = 0.028

    def get_wrist_position(self, last_event):
        position = np.zeros(3)
        
        # x axis FIXED
        position[0] = self.wrist_yaw_from_base

        #TODO: check if this is correct
        # y depends on Arm Extension
        position[1] = -(last_event.metadata["arm"]["extension_m"] + self.arm_offset)

        # z depends on Lift
        position[2] = last_event.metadata["arm"]["lift_m"] + self.lift_base_offset + self.lift_wrist_offset

        #rotation = np.zeros((3,3))
        print(f"Wrist position from base frame: {position}")
        return position
    

    def find_points_on_y_axis(self, p2, distance=0.210): #0.235 works for apple 0.208
        def distance_between_points(p1, p2):
            x1, y1 = p1
            x2, y2 = p2
            return math.sqrt((x2 - x1)**2 + (y2 - y1)**2)
        
        sqrt_diff = distance**2 - p2[0]**2
        if sqrt_diff < 0:
            return []
        
        y1_1 = p2[1] + math.sqrt(distance**2 - p2[0]**2)
        y1_2 = p2[1] - math.sqrt(distance**2 - p2[0]**2)

        print("new points 1: ", y1_1, distance_between_points(p2,[0.0, y1_1]))
        print("new ppints 2: ", y1_2, distance_between_points(p2,[0.0, y1_2]))

        new_points = []
        if abs(distance_between_points(p2,[0.0, y1_1]) - distance) <= 0.0005:
            new_points.append([0.0, y1_1])
        if abs(distance_between_points(p2,[0.0, y1_2]) - distance) <= 0.0005:
            new_points.append([0.0, y1_2])

        return new_points #returns bigger value first - closer to 0 means it's cloer to base


    def plan_grasp_trajectory(self, object_position, last_event):
        wrist_position = self.get_wrist_position(last_event)

        x_delta, y_delta, z_delta = (object_position - wrist_position)
        distance = math.sqrt(x_delta**2 + y_delta**2)
        isReachable=False
        print("Before Extension X delta and Y delta: ", x_delta, y_delta)

        trajectory = []
        if abs(distance - 0.205) <= 0.025:
            # don't need to adjust arm extension
            # plan trajectory
            isReachable = True
        
            # open grasper 
            trajectory.append({"action": "MoveGrasp", "args": {"move_scalar":100}})

            # TODO: check z_delta before  - will it hit the object?
            # rotate wrist - stretch wrist moves clockwise
            # pregrasp position 's Y direction is X
            # pregrasp position 's -X direction is Y            
            wrist_offset = np.degrees(np.arctan2(-x_delta, -y_delta)) # arctan2(y,x)
            trajectory.append({"action": "WristTo", "args": {"move_to":  wrist_offset}})

            # lift - will it hit the object? most likely the arm is higher than the object....
            trajectory.append({"action": "MoveArmBase", "args": {"move_scalar": self.plan_lift_extenion(object_position, last_event.metadata["arm"]["lift_m"])}})

        else:
            curr_arm = last_event.metadata["arm"]["extension_m"]
            new_wrist_positions = self.find_points_on_y_axis([x_delta, y_delta])

            for new_position in new_wrist_positions:
                # TODO: update minmax threshold
                new_arm_position = -new_position[1] 
                if not isReachable and (curr_arm + new_arm_position) < 0.5193114280700684 and (curr_arm + new_arm_position) > 0.0:
                    # open grasper 
                    trajectory.append({"action": "MoveGrasp", "args": {"move_scalar":100}})

                    # TODO: check z_delta before  
                    # - will it hit the object? It does sometimes...so might have to lift a little
                    # rotate wrist - stretch wrist moves clockwise
                    # pregrasp position 's -Y direction is X
                    # pregrasp position 's -X direction is Y
                    last_event.metadata["arm"]["extension_m"] += new_arm_position
                    #wrist_position = self.get_wrist_position(last_event)
                    #x_delta, y_delta, z_delta = (object_position - wrist_position)

                    y_delta = -1*abs(y_delta + new_arm_position)
                    print("After Extension X delta and Y delta: ", x_delta, y_delta)
                    wrist_offset = np.degrees(np.arctan2(-x_delta, -y_delta)) # arctan2(y,x)
                    print(wrist_offset)

                    if wrist_offset >= 75.0: #max Wrist Rotation
                        trajectory = []
                        isReachable=False
                        continue 
                    trajectory.append({"action": "WristTo", "args": {"move_to":  wrist_offset}})

                    # TODO: check z_delta before  - will it hit the object?
                    # extend arm
                    trajectory.append({"action": "MoveArmExtension", "args": {"move_scalar": new_arm_position}})                    
                    isReachable=True 

                    
                    # lift - will it hit the object? most likely the arm is higher than the object....
                    trajectory.append({"action": "MoveArmBase", "args": {"move_scalar": self.plan_lift_extenion(object_position, last_event.metadata["arm"]["lift_m"])}})
                     
                
            
        return isReachable, {"action": trajectory}


        
