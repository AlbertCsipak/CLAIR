using System;
using System.Collections.Generic;
using System.Drawing;
using Unity.Barracuda;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using Vuforia;

public class ARDetector : MonoBehaviour
{
    //vuforia
    PixelFormat mPixelFormat = PixelFormat.RGB888; // Editor passes in a RGBA8888 texture instead of RGB888

    private bool mFormatRegistered = false;
    private Texture2D texture;
    //vuforia

    public NNModel srcModel;
    public TextAsset labelsAsset;
    public RawImage rawImage;
    public Transform displayLocation;
    public Text MyDebug;

    public GameObject midAirObject;

    private RenderTexture _buffer;
    private Model model;
    private IWorker engine;
    private float confidenceThreshold = 0.4f;
    private string[] labels;
    private int counter = 0;

    private const int amountOfClasses = 80;
    //ezeket állitani kell a modelltól függően
    private const int box20Sections = 20;
    private const int box40Sections = 40;
    private const int anchorBatchSize = 85;
    //ezt is 
    private const int inputResolutionX = 640;
    private const int inputResolutionY = 640;

    //model output returns box scales relative to the anchor boxes, 3 are used for 40x40 outputs and another 3 for 20x20 outputs,
    //each cell has 3 boxes 3x85=255
    private readonly float[] anchors = { 10, 14, 23, 27, 37, 58, 81, 82, 135, 169, 319, 344 };

    public struct Box
    {
        public float x;
        public float y;
        public float width;
        public float height;
        public string label;
        public int anchorIndex;
        public int cellIndexX;
        public int cellIndexY;
    }

    public struct PixelBox
    {
        public float x;
        public float y;
        public float width;
        public float height;
        public string label;
    }

    void Start()
    {
        //Screen.fullScreen = true;
        VuforiaApplication.Instance.OnVuforiaStarted += RegisterFormat;
        VuforiaBehaviour.Instance.World.OnStateUpdated += OnVuforiaUpdated;

        _buffer = new RenderTexture(inputResolutionX, inputResolutionY,0);

        Application.targetFrameRate = 30;
        Screen.orientation = ScreenOrientation.LandscapeLeft;

        labels = labelsAsset.text.Split('\n');

        model = ModelLoader.Load(srcModel);

    }
    void Update()
    {
        Graphics.Blit(texture, _buffer,new Vector2(1f,1f),new Vector2(0,0));

        VerticallyFlipRenderTexture(_buffer);

        //check what will enter the algorhitm
        rawImage.texture = _buffer;
        
        //clear previous boxes and labels on every x frames
        if (counter % 5 == 0)
        {
            counter = 0;
            foreach (Transform child in displayLocation)
            {
                Destroy(child.gameObject);
            }
        }
        counter++;

        MyDebug.text  = "X:" + VuforiaBehaviour.Instance.transform.position.x.ToString()+"\n";
        MyDebug.text += "Y:" + VuforiaBehaviour.Instance.transform.position.y.ToString() + "\n";
        MyDebug.text += "Z:" + VuforiaBehaviour.Instance.transform.position.z.ToString() + "\n";

        MyDebug.text += "W:" + VuforiaBehaviour.Instance.transform.rotation.w.ToString() + "\n";
        MyDebug.text += "Y:" + VuforiaBehaviour.Instance.transform.rotation.x.ToString() + "\n";
        MyDebug.text += "X:" + VuforiaBehaviour.Instance.transform.rotation.z.ToString() + "\n";

        //MyDebug.text += "OX:" + midAirObject.transform.localPosition.x.ToString() + "\n";
        //MyDebug.text += "OY:" + midAirObject.transform.localPosition.y.ToString() + "\n";
        //MyDebug.text += "OZ:" + midAirObject.transform.localPosition.z.ToString() + "\n";

        MyDebug.text += rawImage.mainTexture.height.ToString() + "\n";
        MyDebug.text += rawImage.mainTexture.width.ToString() + "\n";

        //MyDebug.text += texture.height + "\n";
        //MyDebug.text += texture.width + "\n";

        MyDebug.text += VuforiaBehaviour.Instance.CameraDevice.GetRecommendedFPS()+"\n";

        ExecuteML();
    }
    void OnVuforiaUpdated()
    {
        if (mFormatRegistered)
        {
            Vuforia.Image image = VuforiaBehaviour.Instance.CameraDevice.GetCameraImage(mPixelFormat);
            texture = new Texture2D(image.BufferWidth, image.BufferHeight, TextureFormat.RGB24, false);

            image.CopyBufferToTexture(texture);
            texture.Apply();
        }
        else
        {
            RegisterFormat();
        }
    }

    public static void VerticallyFlipRenderTexture(RenderTexture target)
    {
        var temp = RenderTexture.GetTemporary(target.descriptor);
        Graphics.Blit(target, temp, new Vector2(1, -1), new Vector2(0, 1));
        Graphics.Blit(temp, target);
        RenderTexture.ReleaseTemporary(temp);
    }
    void DrawBox(PixelBox box)
    {
        //object detection ar cameran mert ugy sokkal egyszerübb lesz elmenteni a helyeket ha már meglévő kordinátán ismeri fel az objektumokat

        //add bounding box
        //GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        //cube.name = "ObjectCube";
        //cube.transform.localPosition = new Vector3(VuforiaBehaviour.Instance.transform.position.x + 5, VuforiaBehaviour.Instance.transform.position.y + (box.y) / 500 - box.height / 1000, VuforiaBehaviour.Instance.transform.position.z + (-box.x) / 500 + box.width / 1000);

        GameObject panel = new GameObject("ObjectBox");
        panel.transform.SetParent(displayLocation, false);
        panel.AddComponent<CanvasRenderer>();
        UnityEngine.UI.Image img = panel.AddComponent<UnityEngine.UI.Image>();
        img.color = new UnityEngine.Color(1, 0, 0, 0.2f);
        panel.transform.localPosition = new Vector3(box.x, -box.y);
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(box.width*1.1f, box.height*1.1f);

        //add class label
        GameObject text = new GameObject("ObjectLabel");
        text.AddComponent<CanvasRenderer>();
        Text txt = text.AddComponent<Text>();
        text.transform.SetParent(panel.transform, false);
        txt.text = box.label/*+" x:"+box.x+" y:"+box.y*/;
        txt.color = new UnityEngine.Color(0, 1, 0, 1f);
        txt.fontSize = 20;
        txt.font = Resources.GetBuiltinResource(typeof(Font), "Arial.ttf") as Font;


        //add mid air object
        if (txt.text.Contains("chair"))
        {
            midAirObject.transform.localPosition = new Vector3(VuforiaBehaviour.Instance.transform.position.x + 3, VuforiaBehaviour.Instance.transform.position.y + (box.y) / 500 - box.height / 1000, VuforiaBehaviour.Instance.transform.position.z + (-box.x) / 500 + box.width / 1000);
        }
    }
    void RegisterFormat()
    {
        bool success = VuforiaBehaviour.Instance.CameraDevice.SetFrameFormat(mPixelFormat, true);
        if (success)
        {
            mFormatRegistered = true;
        }
        else
        {
            mFormatRegistered = false;
        }
    }
    void ExecuteML()
    {

        engine = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, model);

        //preprocess image for input
        var input = new Tensor(_buffer, 3);
        engine.Execute(input);
        ;
        //read output tensors
        var output20 = engine.PeekOutput("016_convolutional"); //016_convolutional = original output tensor name for 20x20 boundingBoxes
        var output40 = engine.PeekOutput("023_convolutional"); //023_convolutional = original output tensor name for 40x40 boundingBoxes
        ;
        //this list is used to store the original model output data
        List<Box> outputBoxList = new List<Box>();

        //this list is used to store the values converted to intuitive pixel data
        List<PixelBox> pixelBoxList = new List<PixelBox>();

        //decode the output 
        outputBoxList = DecodeOutput(output20, output40);

        //convert output to intuitive pixel data (x,y coords from the center of the image; height and width in pixels)
        pixelBoxList = ConvertBoxToPixelData(outputBoxList);

        //draw bounding boxes
        for (int i = 0; i < pixelBoxList.Count; i++)
        {
            DrawBox(pixelBoxList[i]);
        }

        //clean memory
        input.Dispose();
        engine.Dispose();
        Resources.UnloadUnusedAssets();
    }

    List<Box> DecodeOutput(Tensor output20, Tensor output40)
    {
        List<Box> outputBoxList = new List<Box>();

        //decode results into a list for each output(20x20 and 40x40), anchor mask selects the output box presets (first 3 or the last 3 presets) 
        outputBoxList = DecodeYolo(outputBoxList, output40, box40Sections, 0);
        outputBoxList = DecodeYolo(outputBoxList, output20, box20Sections, 3);

        return outputBoxList;
    }

    List<Box> DecodeYolo(List<Box> outputBoxList, Tensor output, int boxSections, int anchorMask)
    {
        for (int boundingBoxX = 0; boundingBoxX < boxSections; boundingBoxX++)
        {
            for (int boundingBoxY = 0; boundingBoxY < boxSections; boundingBoxY++)
            {
                for (int anchor = 0; anchor < 3; anchor++)
                {
                    if (output[0, boundingBoxX, boundingBoxY, anchor * anchorBatchSize + 4] > confidenceThreshold)
                    {
                        //identify the best class
                        float bestValue = 0;
                        int bestIndex = 0;
                        for (int i = 0; i < amountOfClasses; i++)
                        {
                            float value = output[0, boundingBoxX, boundingBoxY, anchor * anchorBatchSize + 5 + i];
                            if (value > bestValue)
                            {
                                bestValue = value;
                                bestIndex = i;
                            }
                        }
                        Debug.Log(labels[bestIndex] + " " + output[0, boundingBoxX, boundingBoxY, anchor * anchorBatchSize + 4]);

                        Box tempBox;
                        tempBox.x = output[0, boundingBoxX, boundingBoxY, anchor * anchorBatchSize];
                        tempBox.y = output[0, boundingBoxX, boundingBoxY, anchor * anchorBatchSize + 1];
                        tempBox.width = output[0, boundingBoxX, boundingBoxY, anchor * anchorBatchSize + 2];
                        tempBox.height = output[0, boundingBoxX, boundingBoxY, anchor * anchorBatchSize + 3];
                        tempBox.label = labels[bestIndex];
                        tempBox.anchorIndex = anchor + anchorMask;
                        tempBox.cellIndexY = boundingBoxX;
                        tempBox.cellIndexX = boundingBoxY;
                        outputBoxList.Add(tempBox);

                    }
                }
            }
        }
        return outputBoxList;
    }

    List<PixelBox> ConvertBoxToPixelData(List<Box> boxList)
    {
        List<PixelBox> pixelBoxList = new List<PixelBox>();
        for (int i = 0; i < boxList.Count; i++)
        {
            PixelBox tempBox;

            //apply anchor mask, each output uses a different preset box
            var boxSections = boxList[i].anchorIndex > 2 ? box20Sections : box40Sections;

            //move marker to the edge of the picture -> move to the center of the cell -> add cell offset (cell size * amount of cells) -> add scale
            tempBox.x = (float)(-inputResolutionX * 0.5) + inputResolutionX / boxSections * 0.5f + inputResolutionX / boxSections * boxList[i].cellIndexX + Sigmoid(boxList[i].x);
            tempBox.y = (float)(-inputResolutionY * 0.5) + inputResolutionX / boxSections * 0.5f + inputResolutionX / boxSections * boxList[i].cellIndexY + Sigmoid(boxList[i].y);

            //select the anchor box and multiply it by scale
            tempBox.width = anchors[boxList[i].anchorIndex * 2] * (float)Math.Pow(Math.E, boxList[i].width);
            tempBox.height = anchors[boxList[i].anchorIndex * 2 + 1] * (float)Math.Pow(Math.E, boxList[i].height);
            tempBox.label = boxList[i].label;
            pixelBoxList.Add(tempBox);
        }

        return pixelBoxList;
    }

    float Sigmoid(float value)
    {
        return 1.0f / (1.0f + (float)Math.Exp(-value));
    }
}