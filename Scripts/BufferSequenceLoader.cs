using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Pcx;
using StackExchange.Redis;
using Unity.Collections;
using Unity.Jobs;
using Unity.WebRTC;
using UnityEditor;
using UnityEngine;
using VolPlayer;
using UnityEngine.Profiling;

public class BufferSequenceLoader : MonoBehaviour
{
    public string FileCommon;
    public int LoadFrameCount;
    public ComputeBuffer[] BufferSequence;
    private PointCloudRenderer _renderer;

    public void LoadPlySequence(string fileCommon, int n_frame)
    {
        UnloadPlySequence();
        BufferSequence = new ComputeBuffer[n_frame];

        int counter = 0;
        int i = 0;
        int maxGap = 100;
        int gapCounter = 0;
        while (counter < n_frame && gapCounter < maxGap)
        {
            string filePath = fileCommon.Replace("*", i.ToString());
            if(File.Exists(filePath)){
                BufferSequence[counter] = PlyLoader.LoadPly(filePath);
                counter += 1;
                gapCounter = 0;
            }else
            {
                if (counter==0) continue;
                BufferSequence[counter] = BufferSequence[counter - 1];
                counter += 1;
                //gapCounter += 1;
            }

            i += 1;
        }
    }
    
    public void LoadPlySequenceFromBinary(string fileCommon, int n_frame)
    {
        UnloadPlySequence();
        
        BufferSequence = new ComputeBuffer[n_frame];

        int counter = 0;
        int i = 0;
        int maxGap = 100;
        int gapCounter = 0;
        
        PlyLoader.PrintTime();
        while (counter < n_frame && gapCounter < maxGap)
        {
            string filePath = fileCommon.Replace("*", i.ToString());
            if (File.Exists(filePath)) {
                BufferSequence[counter] = PlyLoader.LoadPlyFromBin(filePath);
                counter += 1;
                gapCounter = 0;
            } else {
                if (counter==0) continue;
                BufferSequence[counter] = BufferSequence[counter - 1];
                counter += 1;
            }
        
            i += 1;
        }
        
        PlyLoader.PrintTime();
    }

    [HideInInspector]
    public int _frameNumber;
    [HideInInspector]
    public bool _isPlaying;
    public bool recursivePlay;
    public int frameRate = 60;
    private float _timer;

    public void Play()
    {
        _renderer = GetComponent<PointCloudRenderer>();
        _frameNumber = 1;
        _timer = 0;
        _isPlaying = true;
    }

    private static HttpClient httpCli;
    private static ConnectionMultiplexer redis;
    private static IDatabase redisDb;
    public async void Start()
    {
        return;
        const int frameCount = 408;
        const int elementSize = sizeof(float) * 4;
        
        BufferSequence = new ComputeBuffer[frameCount];
        
        // use redis
        redis = ConnectionMultiplexer.Connect("127.0.0.1:6379");
        redisDb = redis.GetDatabase(0);
        for (int i = 0; i < frameCount; i++)
        {
            var res = await redisDb.HashGetAsync("news_interview", $"bin:{i}");
            // Debug.Log($"Downloaded {i}, count = {res.Length / elementSize}");
            var pointBuffer = new ComputeBuffer((int) res.Length() / elementSize, elementSize);
            pointBuffer.SetData(res);
            BufferSequence[i] = pointBuffer;
            loadedFrameIndex = i;
        }

    }

    private int loadedFrameIndex = 0;

    public void OnDestroy()
    {
        UnloadPlySequence();
    }

    public void Update()
    {
        // pause on buffering
        if (BufferSequence != null && _frameNumber > loadedFrameIndex && loadedFrameIndex != BufferSequence.Length - 1) return;
        if (BufferSequence!=null && _frameNumber >= BufferSequence.Length)
        {
            if (!recursivePlay)
            {
                _isPlaying = false;
                _frameNumber = 0;
                _timer = 0;
            }
            else
            {
                _frameNumber = 0;
                _timer = 0;
            }
        }
        
        if (BufferSequence!=null && _isPlaying)
        {
            if (frameRate<0)
            {
                _renderer.sourceBuffer = BufferSequence[_frameNumber];
                _frameNumber += 1;
            }
            else
            {
                _renderer.sourceBuffer = BufferSequence[_frameNumber];
                _timer += Time.deltaTime;
                _frameNumber = (int)(_timer * frameRate);
            }
        }
    }

    public void UnloadPlySequence()
    {
        _renderer = GetComponent<PointCloudRenderer>();
        _renderer.sourceBuffer = null;
        if (BufferSequence!=null)
        for (int i = 0; i < BufferSequence.Length; i++)
        {
            if (BufferSequence[i] == null) continue;
            BufferSequence[i].Release();
        }
        BufferSequence = null;
    }
}

[CustomEditor(typeof(BufferSequenceLoader))]
class BufferSequenceLoaderEditor : Editor
{
    private BufferSequenceLoader loader;
    
    private void Awake()
    {
        loader = (BufferSequenceLoader)target;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        // if (GUILayout.Button("Load ply ASCII (Deprecated)"))
        // {
        //     loader.LoadPlySequence(loader.FileCommon, loader.LoadFrameCount);
        // }
        
        if (GUILayout.Button("Load ply Binary"))
        {
            loader.LoadPlySequenceFromBinary(loader.FileCommon, loader.LoadFrameCount);
            PlyLoader.PrintTime("quit UnloadPlySequence");
        }
        if (GUILayout.Button("Release GPU Buffer"))
        {
            loader.UnloadPlySequence();
        }

        if (loader.BufferSequence!=null && loader.BufferSequence.Length > 0 && loader.BufferSequence[0]!=null)
        {
            EditorGUILayout.HelpBox(
                "Loaded: " + loader.BufferSequence.Length + "(frames) x " + loader.BufferSequence[0].count +
                "(points);", MessageType.None);
        }

        if (EditorApplication.isPlaying)
        {
            if (GUILayout.Button("Play"))
            {
                loader.Play();
            }
            EditorGUILayout.HelpBox(
                "Playing: "+loader._isPlaying+"; Frame Number" + loader._frameNumber + ";", MessageType.None);
        }
    }
}
