using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Text;
using System.IO;
using System;

public class DownloadThorAssets : MonoBehaviour
{
    public string savePath = "Assets/ThorAssets";
    public string assetPath = "Assets/Physics/SimObjsPhysics";
    public string materialPath = "Assets/Resources/QuickMaterials";
    //public string doorAssetPath = "Assets/Physics/SimObjsPhysics/ManipulaTHOR Objects/Doorways/Prefabs";
    public bool applyBoundingBox = false;
    public bool saveSubMeshes = true;
    public bool saveSubMeshTransform = true;

    Dictionary<string, Material> allMaterials = new Dictionary<string, Material>();
    Dictionary<string, Dictionary<string, string>> Mat2Texture = new Dictionary<string, Dictionary<string, string>>();

    public bool skipMeshExport = false;
    public bool skipMaterialExport = true;

    [System.Serializable]
    public class SerializableKeyValuePair
    {
        public string outerKey;
        public string innerKey;
        public string value;

        public SerializableKeyValuePair(string outerKey, string innerKey, string value)
        {
            this.outerKey = outerKey;
            this.innerKey = innerKey;
            this.value = value;
        }
    }

    [System.Serializable]
    public class SerializableDictionary
    {
        public List<SerializableKeyValuePair> keyValuePairs;

        public SerializableDictionary(Dictionary<string, Dictionary<string, string>> dictionary)
        {
            keyValuePairs = new List<SerializableKeyValuePair>();

            foreach (var outerPair in dictionary)
            {
                foreach (var innerPair in outerPair.Value)
                {
                    keyValuePairs.Add(new SerializableKeyValuePair(outerPair.Key, innerPair.Key, innerPair.Value));
                }
            }
        }
    }


    // Start is called before the first frame update
    void Start()
    {
        //Directory.CreateDirectory(Path.Combine(savePath, "Textures"));

        // get all assets and export obj
        GatherGameObjectsFromPrefabsAndSave(assetPath, applyBoundingBox, saveSubMeshes, saveSubMeshTransform);
        //GatherGameObjectsFromPrefabsAndSave(doorAssetPath, false, true);

        if(!skipMaterialExport)
        {
            GetAllMaterials(materialPath);

            // save material dictionary to json
            //Debug.Log(Mat2Texture.Count);
            string json = JsonUtility.ToJson(new SerializableDictionary(Mat2Texture), true);
            File.WriteAllText(Path.Combine(savePath, "quick_material_to_textures.json"), json);
            Debug.Log("Saving material to textures dictionary to: " + Path.Combine(savePath, "material_to_textures.json"));
        }
    }


    void GetAllMaterials(string folderPath)
    {
        if (Directory.Exists(folderPath))
        {
            // Get all .mat files in the folder
            string[] matFiles = Directory.GetFiles(folderPath, "*.mat", SearchOption.AllDirectories);

            // Loop through each .mat file
            foreach (string matFile in matFiles)
            {
                // Load the Material from the .mat file
                Debug.Log("Loading material: " + matFile);
                Material m = AssetDatabase.LoadAssetAtPath<Material>(matFile);
                if (m != null)
                {
                    if (!Mat2Texture.ContainsKey(m.name))
                    {
                        Dictionary<string, string> matdict = new Dictionary<string, string>();
                        string _MainTex = TryExportTexture("_MainTex", m);
                        matdict.Add("_MainTex", _MainTex);
                        string _MetallicGlossMap = TryExportTexture("_MetallicGlossMap", m);
                        matdict.Add("_MetallicGlossMap", _MetallicGlossMap);
                        string _BumpMap = TryExportTexture("_BumpMap", m);
                        matdict.Add("_BumpMap", _BumpMap);

                        matdict.Add("emission_rgba", m.GetColor("_EmissionColor").r.ToString() + " " + m.GetColor("_EmissionColor").g.ToString() + " " + m.GetColor("_EmissionColor").b.ToString() + " " + m.GetColor("_EmissionColor").a.ToString());
                        //matdict.Add("specular_rgba", m.GetColor("_SpecColor").r.ToString() + " " + m.GetColor("_SpecColor").g.ToString() + " " + m.GetColor("_SpecColor").b.ToString());
                        matdict.Add("specular", m.GetFloat("_SpecularHighlights").ToString()); // reflectance ?
                        matdict.Add("smoothness", m.GetFloat("_Glossiness").ToString()); // reflectance ? _Glossiness (Smothness)
                        matdict.Add("metallic", m.GetFloat("_Metallic").ToString());  // shininess ? _GlossyReflectons (Glossy Reflections) or _Metallic
                        //matdict.Add("reflection", m.GetFloat("_GlossyReflectons").ToString());  // shininess ? _GlossyReflectons (Glossy Reflections) or _Metallic
                        matdict.Add("albedo_rgba", m.color.r.ToString() + " " + m.color.g.ToString() + " " + m.color.b.ToString() + " " + m.color.a.ToString());
                        Debug.Log(m.color.r.ToString() + " " + m.color.g.ToString() + " " + m.color.b.ToString() + " " + m.color.a.ToString());

                        Mat2Texture.Add(m.name, matdict);
                        Debug.Log("Adding " + m.name);
                        //Debug.Log("Adding " + m.name + " to Mat2Texture" + Mat2Texture[m.name]["_MainTex"]);
                    }
                }
            }
        }
        else
        {
            Debug.LogError("Folder does not exist: " + folderPath);
        }
    }

    string GetRelativePath(string rootDirectory, string fullPath)
    {
        string[] splitArray =  fullPath.Split(char.Parse("/"));

        string relativePath = "";
        for (int i = 3; i < splitArray.Length; i++)
        {
            relativePath += splitArray[i];
            if (i<splitArray.Length-1)
                relativePath += "/";
        }

        return relativePath;
    }

    void GatherGameObjectsFromPrefabsAndSave(string directoryPath, bool applyBoundingBox = false, bool saveSubMeshes = false, bool saveSubMeshTransform = false)
    {
        if (!Directory.Exists(directoryPath))
        {
            Debug.LogError("Directory does not exist: " + directoryPath);
            return;
        }

        string[] prefabFiles = Directory.GetFiles(directoryPath, "*.prefab", SearchOption.AllDirectories);


        foreach (string prefabPath in prefabFiles)
        {
            // skip if already exist
            //if (File.Exists(Path.Combine(savePath, GetRelativePath(assetPath, prefabPath).Replace(".prefab", ".obj"))))
            //{
            //    Debug.Log("Skipping " + prefabPath);
            //    continue;
            //}

            string relativePrefabPath = GetRelativePath(assetPath, prefabPath);
            Debug.Log("Prefab path: " + relativePrefabPath);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                GameObject instantiatedPrefab = Instantiate(prefab);
                SaveEachAsset(instantiatedPrefab, relativePrefabPath, applyBoundingBox, saveSubMeshes, saveSubMeshTransform);
                Destroy(instantiatedPrefab);
            }
            else
            {
                Debug.LogWarning("Failed to load prefab at path: " + prefabPath);
            }
        }
    }

    
    void SaveEachAsset(GameObject go, string relativeExportPath, bool applyBoundingBox = true, bool saveSubMeshes = false, bool saveSubMeshTransform = false)
    {        
        Directory.CreateDirectory(Path.Combine(savePath, Path.GetDirectoryName(relativeExportPath)));
        
        // save mesh as obj file
        MeshFilter[] meshFilters = go.transform.GetComponentsInChildren<MeshFilter>();

        Vector3 center = Vector3.zero;
        SimObjPhysics parent = go.transform.GetComponent<SimObjPhysics>();

        if(parent != null)  
        {
            AxisAlignedBoundingBox box = parent.AxisAlignedBoundingBox;
            center = box.center;
            //Debug.Log("center" + center.ToString());
        }       
        else
        {
            //BoxCollider bbox = go.GetComponent<BoxCollider>();
            //if(bbox != null)
            //    center = bbox.center;
            //else
            Debug.Log("No bounding box found for " + go.name);
        } 
    
        Debug.Log("saving mesh1" + center.ToString());

        SaveMeshes(relativeExportPath, meshFilters, center, applyBoundingBox, saveSubMeshes, saveSubMeshTransform);
        Debug.Log("saving mesh2");

        if (!skipMaterialExport)
        {
            SaveMaterials(relativeExportPath);
            allMaterials.Clear();
        }

    }

    void SaveMaterials(string relativeExportPath)
    {
        
        string baseFileName = Path.GetFileNameWithoutExtension(relativeExportPath);

        StringBuilder sbMaterials = new StringBuilder();
        foreach (KeyValuePair<string, Material> entry in allMaterials)
        {
            sbMaterials.Append(MaterialToString(entry.Value));
            sbMaterials.AppendLine();
        }
    
        //write to disk
        System.IO.File.WriteAllText( Path.Combine(Path.Combine(savePath, Path.GetDirectoryName(relativeExportPath)), baseFileName + ".mtl"),  sbMaterials.ToString());
        print("material saved");
    }

    void SaveMeshes(string relativeExportPath, MeshFilter[] meshFilters, Vector3 center, bool applyBoundingBox = true, bool saveSubMeshes = false, bool saveSubMeshTransform = false)
    {
        Debug.Log("saving mesh");

        string baseFileName = Path.GetFileNameWithoutExtension(relativeExportPath);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("mtllib " + baseFileName + ".mtl");
        int lastIndex = 0;
    

        Dictionary<string, Dictionary<string, string>> mesh_transforms = new Dictionary<string, Dictionary<string, string>>();
        mesh_transforms["bbox_center"] = new Dictionary<string, string>();
        mesh_transforms["bbox_center"]["position"] = center.ToString("0.00000");
        for(int i = 0; i < meshFilters.Length; i++)
        {
            if(saveSubMeshes)
            {
                sb = new StringBuilder();
                sb.AppendLine("mtllib " + baseFileName + ".mtl");
                lastIndex = 0;
            }

            string meshName = meshFilters[i].gameObject.name; //+ "_" + i.ToString();
            Debug.Log(meshName);

            MeshFilter mf = meshFilters[i];

            if (mf == null)
            {
                Debug.LogError("No mesh filter found for " + meshName);
                continue;
            }

            

            mesh_transforms[meshName + "_" +i.ToString()] = new Dictionary<string, string>();
            //mesh_transforms[meshName + "_" +i.ToString()]["position"] = mf.gameObject.transform.localPosition.ToString("0.00000");
            //mesh_transforms[meshName + "_" +i.ToString()]["rotation"] = mf.gameObject.transform.localEulerAngles.ToString("0.00000");

            Transform parent = mf.gameObject.transform.parent;
            if(parent == null)
            {
                mesh_transforms[meshName + "_" +i.ToString()]["name"] = mf.gameObject.transform.name;
                mesh_transforms[meshName + "_" +i.ToString()]["parentName"] = "root";
                mesh_transforms[meshName + "_" +i.ToString()]["localparentPosition"] = mf.gameObject.transform.localPosition.ToString("0.00000"); // body
                mesh_transforms[meshName + "_" +i.ToString()]["localparentRotation"] = mf.gameObject.transform.localRotation.ToString("0.00000");//localEulerAngles.ToString("0.00000");
            }
            else
            {
                mesh_transforms[meshName + "_" +i.ToString()]["name"] = mf.gameObject.transform.parent.name;
                if(mf.gameObject.transform.parent.transform.parent != null)
                    mesh_transforms[meshName + "_" +i.ToString()]["parentName"] = mf.gameObject.transform.parent.transform.parent.name;
                else
                    mesh_transforms[meshName + "_" +i.ToString()]["parentName"] = "root";
                mesh_transforms[meshName + "_" +i.ToString()]["localparentPosition"] = mf.gameObject.transform.parent.localPosition.ToString("0.00000"); // body
                mesh_transforms[meshName + "_" +i.ToString()]["localparentRotation"] = mf.gameObject.transform.parent.localRotation.ToString("0.00000");//localEulerAngles.ToString("0.00000");
            }
            mesh_transforms[meshName + "_" +i.ToString()]["localPosition"] = mf.gameObject.transform.localPosition.ToString("0.00000"); // geom
            mesh_transforms[meshName + "_" +i.ToString()]["localRotation"] = mf.gameObject.transform.localRotation.ToString("0.00000");// geom

            Mesh msh = mf.sharedMesh;
            if (msh == null)
            {
                Debug.LogError("No mesh found for " + mf.gameObject.name);
                continue;
            }

            MeshRenderer mr = mf.gameObject.GetComponent<MeshRenderer>();
            {
                string exportName = meshName;
                if (true)
                {
                    exportName += "_" + i;
                }
                sb.AppendLine("g " + exportName);
            }


            if(mr != null)
            {
                Material[] mats = mr.sharedMaterials;

                for(int j=0; j < mats.Length; j++)
                {
                    Material m = mats[j];
                    if (m != null)
                    {
                        if (!allMaterials.ContainsKey(m.name))
                        {
                            allMaterials[m.name] = m;
                        }
                    }
                    else
                        Debug.LogWarning("No material found for " + meshName);
                }
            }
            else
            {
                Debug.LogWarning("No mesh renderer found for " + meshName);
            }

            if (skipMeshExport)
                continue;

            int faceOrder = (int)Mathf.Clamp((mf.gameObject.transform.lossyScale.x * mf.gameObject.transform.lossyScale.z), -1, 1);

            //export vector data (FUN :D)!
            foreach (Vector3 vx in msh.vertices)
            {
                Vector3 v = vx;
                if (true) //applyScale)
                {
                    v = MultiplyVec3s(v, mf.gameObject.transform.lossyScale);
                }
                
                if (!saveSubMeshes) //true) //applyRotation)
                {
  
                    v = RotateAroundPoint(v, Vector3.zero, mf.gameObject.transform.rotation);
                    //v = RotateAroundPoint(v, Vector3.zero, mf.gameObject.transform.localRotation);

                }

                if (!saveSubMeshes) //true) //applyPosition)
                {
                    v += mf.gameObject.transform.position;
                    //v += mf.gameObject.transform.localPosition;
                }

                if (applyBoundingBox) //true)// move to bouning box center
                    v -= center;                

                v.x *= -1;
                sb.AppendLine("v " + v.x + " " + v.y + " " + v.z);
            }

            foreach (Vector3 vx in msh.normals)
            {
                Vector3 v = vx;
                
                if (true) //applyScale)
                {
                    v = MultiplyVec3s(v, mf.gameObject.transform.lossyScale.normalized);
                }
                if (!saveSubMeshes) //applyRotation)
                {
                    v = RotateAroundPoint(v, Vector3.zero, mf.gameObject.transform.rotation);
                    //v = RotateAroundPoint(v, Vector3.zero, mf.gameObject.transform.localRotation);
                }
                if (!saveSubMeshes) //true) //applyPosition)
                {
                    v += mf.gameObject.transform.position;
                    //v += mf.gameObject.transform.localPosition;
                }

                if (applyBoundingBox) //true)// move to bouning box center
                    v -= center;    

                v.x *= -1;
                sb.AppendLine("vn " + v.x + " " + v.y + " " + v.z);

            }

            foreach (Vector2 v in msh.uv)
            {
                sb.AppendLine("vt " + v.x + " " + v.y);
            }

            for (int j=0; j < msh.subMeshCount; j++)
            {
                if(mr != null && j < mr.sharedMaterials.Length)
                {
                    if(mr.sharedMaterials[j] != null)
                    {
                        string matName = mr.sharedMaterials[j].name;
                        sb.AppendLine("usemtl " + matName);
                    }
                    else
                    {
                        sb.AppendLine("usemtl " + meshName + "_sm" + j);
                    }
                }
                else
                {
                    sb.AppendLine("usemtl " + meshName + "_sm" + j);
                }

                int[] tris = msh.GetTriangles(j);
                for(int t = 0; t < tris.Length; t+= 3)
                {
                    int idx2 = tris[t] + 1 + lastIndex;
                    int idx1 = tris[t + 1] + 1 + lastIndex;
                    int idx0 = tris[t + 2] + 1 + lastIndex;
                    if(faceOrder < 0)
                    {
                        sb.AppendLine("f " + ConstructOBJString(idx2) + " " + ConstructOBJString(idx1) + " " + ConstructOBJString(idx0));
                    }
                    else
                    {
                        sb.AppendLine("f " + ConstructOBJString(idx0) + " " + ConstructOBJString(idx1) + " " + ConstructOBJString(idx2));
                    }
                    
                }
            }

            if(saveSubMeshes)
            {
                //write to disk
                Debug.Log("writing to disk: " + Path.Combine(savePath, Path.Combine(Path.GetDirectoryName(relativeExportPath), baseFileName + "_" + i.ToString() + ".obj")));
                System.IO.File.WriteAllText( Path.Combine(savePath,  Path.Combine(Path.GetDirectoryName(relativeExportPath), baseFileName + "_" + i.ToString() + ".obj")), sb.ToString());
                Debug.Log("Write to disk done");
            }

            lastIndex += msh.vertices.Length;
        }


        if (skipMeshExport)
            return;

        if(!saveSubMeshes)
        {
            //write to disk
            Debug.Log("writing to disk: " + Path.Combine(savePath, Path.Combine(Path.GetDirectoryName(relativeExportPath), baseFileName + ".obj")));
            System.IO.File.WriteAllText( Path.Combine(savePath,  Path.Combine(Path.GetDirectoryName(relativeExportPath), baseFileName + ".obj")), sb.ToString());
            Debug.Log("Write to disk done");
            print("mesh saved");
        }

        if (saveSubMeshes & saveSubMeshTransform)  
        {
            string json = JsonUtility.ToJson(new SerializableDictionary(mesh_transforms), true);
            File.WriteAllText(Path.Combine(savePath, Path.Combine(Path.GetDirectoryName(relativeExportPath), baseFileName + ".json")), json);
            Debug.Log("Saving mesh transform dictionary.");
        }    
        
    }

    Vector3 RotateAroundPoint(Vector3 point, Vector3 pivot, Quaternion angle)
    {
        return angle * (point - pivot) + pivot;
    }

    Vector3 MultiplyVec3s(Vector3 v1, Vector3 v2)
    {
        return new Vector3(v1.x * v2.x, v1.y * v2.y, v1.z * v2.z);
    }

    private string ConstructOBJString(int index)
    {
        string idxString = index.ToString();
        return idxString + "/" + idxString + "/" + idxString;
    }

    string MaterialToString(Material m)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("newmtl " + m.name);


        //add properties
        if (m.HasProperty("_Color"))
        {
            sb.AppendLine("Kd " + m.color.r.ToString() + " " + m.color.g.ToString() + " " + m.color.b.ToString());
            if (m.color.a < 1.0f)
            {
                //use both implementations of OBJ transparency
                sb.AppendLine("Tr " + (1f - m.color.a).ToString());
                sb.AppendLine("d " + m.color.a.ToString());
            }
        }
        if (m.HasProperty("_SpecColor"))
        {
            Color sc = m.GetColor("_SpecColor");
            sb.AppendLine("Ks " + sc.r.ToString() + " " + sc.g.ToString() + " " + sc.b.ToString());
        }
        if (true) 
        {
            //diffuse
            string _MainTex = TryExportTexture("_MainTex", m);
            if (_MainTex != "false")
            {
                sb.AppendLine("map_Kd " + _MainTex);
            }
            //spec map
            string _MetallicGlossMap = TryExportTexture("_MetallicGlossMap", m);
            if (_MetallicGlossMap != "false")
            {
                sb.AppendLine("map_Ks " + _MetallicGlossMap);
            }
            //bump map
            string _BumpMap = TryExportTexture("_BumpMap", m);
            if (_BumpMap != "false")
            {
                sb.AppendLine("map_Bump " + _BumpMap);
            }

            if (!Mat2Texture.ContainsKey(m.name))
            {
                Dictionary<string, string> matdict = new Dictionary<string, string>();
                matdict.Add("_MainTex", _MainTex);
                matdict.Add("_MetallicGlossMap", _MetallicGlossMap);
                matdict.Add("_BumpMap", _BumpMap);

                matdict.Add("emission_rgba", m.GetColor("_EmissionColor").r.ToString() + " " + m.GetColor("_EmissionColor").g.ToString() + " " + m.GetColor("_EmissionColor").b.ToString() + " " + m.GetColor("_EmissionColor").a.ToString());
                //matdict.Add("specular_rgba", m.GetColor("_SpecColor").r.ToString() + " " + m.GetColor("_SpecColor").g.ToString() + " " + m.GetColor("_SpecColor").b.ToString());
                matdict.Add("specular", m.GetFloat("_SpecularHighlights").ToString()); // reflectance ?
                matdict.Add("smoothness", m.GetFloat("_Glossiness").ToString()); // reflectance ? _Glossiness (Smothness)
                matdict.Add("metallic", m.GetFloat("_Metallic").ToString());  // shininess ? _GlossyReflectons (Glossy Reflections) or _Metallic
                //matdict.Add("reflection", m.GetFloat("_GlossyReflectons").ToString());  // shininess ? _GlossyReflectons (Glossy Reflections) or _Metallic
                matdict.Add("albedo_rgba", m.color.r.ToString() + " " + m.color.g.ToString() + " " + m.color.b.ToString() + " " + m.color.a.ToString());
                Debug.Log(m.color.r.ToString() + " " + m.color.g.ToString() + " " + m.color.b.ToString() + " " + m.color.a.ToString());

                Mat2Texture.Add(m.name, matdict);
                Debug.Log("Adding " + m.name);
                //Debug.Log("Adding " + m.name + " to Mat2Texture" + Mat2Texture[m.name]["_MainTex"]);
            }

        }
        sb.AppendLine("illum 2");
        return sb.ToString();
    }

    string TryExportTexture(string propertyName, Material m)
    {
        if (m.HasProperty(propertyName))
        {
            Texture t = m.GetTexture(propertyName);
            if(t != null)
            {
                return ExportTexture((Texture2D)t);
            }
        }
        return "false";
    }

    string ExportTexture(Texture2D t)
    {
        string assetPath = AssetDatabase.GetAssetPath(t);

        if(File.Exists(assetPath))
        {
            string textureName = Path.GetFileName(assetPath); // with extension
            string copyPath = Path.Combine(Path.Combine(savePath, "Textures"), textureName);
            Debug.Log(copyPath);

            File.Copy(assetPath, copyPath, true);
            return copyPath;
        }
        else
            return "false";
        /*
        try
        {
            if (autoMarkTexReadable)
            {
                string assetPath = AssetDatabase.GetAssetPath(t);
                Debug.Log(assetPath);

                var tImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (tImporter != null)
                {
                    tImporter.textureType = TextureImporterType.Advanced;

                    if (!tImporter.isReadable)
                    {
                        tImporter.isReadable = true;

                        AssetDatabase.ImportAsset(assetPath);
                        AssetDatabase.Refresh();
                    }
                }
            }
            string exportName = lastExportFolder + "\\" + t.name + ".png";
            Texture2D exTexture = new Texture2D(t.width, t.height, TextureFormat.ARGB32, false);
            exTexture.SetPixels(t.GetPixels());
            System.IO.File.WriteAllBytes(exportName, exTexture.EncodeToPNG());
            return exportName;
        }
        catch (System.Exception ex)
        {
            Debug.Log("Could not export texture : " + t.name + ". is it readable?");
            return "null";
        }
        */

    }
}
