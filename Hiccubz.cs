using Photon.Pun;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace AlienValuable;

public class Hiccubz : MonoBehaviour
{
    private enum State
    {
        Idle, // anim done
        Grabbed, //
        Notice,  // redo
        Stashed,  // sleep anim 
        Flee  // run anim semi-done
    }

    public enum Emotion
    {
        Blank,
        Happy,
        Happy2,
        Notice,
        Sad,
        Scared,
        Angry,
        Curious,
        Hit,
        Sleep,
        Sleep2
    }

    private EnemyNavMeshAgent navMeshAgent;
    private EnemyVision vision;
    private PhotonView photonView;
    private PhysGrabObject physGrabObject;

    private PlayerAvatar playerAvatar;

    private State currentState;
    
    private Quaternion horizontalRotationTarget = Quaternion.identity;
    private Vector3 agentDestination;

    private bool stateImpulse;
    private bool hurtImpulse;

    private float hurtLerp;
    private float stateTimer;

    private int hurtAmount;
    private int defaultLayerMask;

    private Coroutine emotionRoutine;

    public AudioSource audioSource;
    public AudioClip[] fleeSounds;
    public AudioClip curious;
    public AudioClip angry;
    public AudioClip happy;
    public AudioClip sleep;
    public AudioClip sad;


    public SkinnedMeshRenderer facialExpressions;
    public SpringQuaternion horizontalRotationSpring;
    public Material hurtableMaterial;
    public Animator animator;
    public AnimationCurve hurtCurve;
    public Emotion emotion = Emotion.Blank;

    private static readonly Emotion[] allEmotions = (Emotion[])System.Enum.GetValues(typeof(Emotion));

    // I might need to add more / change some from trigger to boolean - I really didnt give this much thought yet

    private static readonly int sitTrigger = Animator.StringToHash("idleSit");
    private static readonly int grabbedTrigger = Animator.StringToHash("grabbed");
    private static readonly int noticeTrigger = Animator.StringToHash("noticeAndStandUp");
    private static readonly int fleeBool = Animator.StringToHash("runAway");
    private static readonly int sleepBool = Animator.StringToHash("sleeping");
    private void Awake()
    {
		navMeshAgent = GetComponent<EnemyNavMeshAgent>();
        vision = GetComponent<EnemyVision>();
        photonView = GetComponent<PhotonView>();
        physGrabObject = GetComponent<PhysGrabObject>();
        hurtAmount = Shader.PropertyToID("_ColorOverlayAmount");
        hurtCurve = AssetManager.instance.animationCurveImpact;
        defaultLayerMask = LayerMask.GetMask("Default");
        UpdateState(State.Idle);

        LevelPoint startHere = SemiFunc.LevelPointGet(base.transform.position, 0f, 15f);

        navMeshAgent.Warp(startHere.transform.position);
    }

    private void Update()
    {
        if (SemiFunc.IsMasterClientOrSingleplayer())
        {
            switch (currentState)
            {
                case State.Idle:
                    StateIdle();
                    break;
                case State.Grabbed:
                    StateGrabbed();
                    break;
                case State.Notice:
                    StateNotice();
                    break;
                case State.Flee:
                    StateFlee();
                    break;
                case State.Stashed:
                    StateStashed();
                    break;
            }
        }
        RotationLogic();
        HurtEffect();
    }

    /* States */

    private void StateIdle()
    {
        if (stateImpulse)
        {
            animator.SetTrigger(sitTrigger);
            stateImpulse = false;

            SetFace(IdleEmotions(), 100f);

            if (playerAvatar)
            {
                UpdateState(State.Notice);
            }

            if (physGrabObject.grabbed)
            {
                navMeshAgent.Disable(9999f);
                UpdateState(State.Grabbed);
            }
        }
    }

    private void StateGrabbed()
    {
        if (stateImpulse)
        {
            animator.SetTrigger(grabbedTrigger);
            stateImpulse = false;
            stateTimer = 1f;

            if (emotionRoutine != null)
                StopCoroutine(emotionRoutine);
            emotionRoutine = StartCoroutine(GrabbedEmotions());

            if (!physGrabObject.grabbed)
            {
                if (InCartOrExtractionPoint())
                {
                    UpdateState(State.Stashed);
                }
                else
                {
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0f)
                    {
                        navMeshAgent.Enable();
                        UpdateState(State.Notice);
                    }
                }
            }
        }
    }

    private void StateNotice()
    {
        if (stateImpulse)
        {
            animator.SetTrigger(noticeTrigger);
            stateImpulse = false;
            stateTimer = 1f;

            SetFace(Emotion.Notice, 100f);
        }
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f && !physGrabObject.grabbed)
        {
            UpdateState(State.Flee);
        }
        if (physGrabObject.grabbed)
        {
            navMeshAgent.Disable(9999f);
            UpdateState(State.Grabbed);
        }
    }

    private void StateFlee()
    {
        if (stateImpulse)
        {
            animator.SetBool(fleeBool, true);
            stateImpulse = false;
            stateTimer = Random.Range(8f, 12f);

            SetFace(Emotion.Scared, 100f);

            LevelPoint levelPoint = SemiFunc.LevelPointGet(base.transform.position, 25f, 999f);
            if (levelPoint != null &&
                NavMesh.SamplePosition(levelPoint.transform.position + Random.insideUnitSphere * 3f, out var hit, 5f, -1) &&
                Physics.Raycast(hit.position, Vector3.down, 5f, defaultLayerMask))
            {
                agentDestination = hit.position;
            }
            else
            {
                UpdateState(State.Idle);
                return;
            }
        }
        else
        {
            navMeshAgent.SetDestination(agentDestination);
            stateTimer -= Time.deltaTime;
            if (stateTimer <= 0f)
            {
                UpdateState(State.Idle);
            }

            if (physGrabObject.grabbed)
            {
                stateTimer = 0f;
                navMeshAgent.Disable(9999f);
                UpdateState(State.Grabbed);
            }
        }
    }

    private void StateStashed()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            animator.SetBool(sleepBool, true);

            SetFace(SleepEmotions(), 100f);

            if (!InCartOrExtractionPoint())
            {
                navMeshAgent.Enable();
                UpdateState(State.Notice);
            }
        }
    }
    private void UpdateState(State state)
    {
        currentState = state;
        stateImpulse = true;
        animator.SetBool(sleepBool, false);
        animator.SetBool(fleeBool, false);
        stateTimer = 0f;
        if (GameManager.Multiplayer())
        {
            photonView.RPC("UpdateStateRPC", RpcTarget.All, currentState);
        }
        else
        {
            UpdateStateRPC(currentState);
        }
    }

    /* Extra Logic */

    private bool InCartOrExtractionPoint()
    {
        return RoundDirector.instance.dollarHaulList.Contains(base.gameObject);
    }

    private void RotationLogic()
    {
        if (navMeshAgent.AgentVelocity.normalized.magnitude > 0.1f)
        {
            horizontalRotationTarget = Quaternion.LookRotation(navMeshAgent.AgentVelocity.normalized);
            horizontalRotationTarget.eulerAngles = new Vector3(0f, horizontalRotationTarget.eulerAngles.y, 0f);
            horizontalRotationSpring.speed = 15f;
            horizontalRotationSpring.damping = 0.8f;
        }
        base.transform.rotation = SemiFunc.SpringQuaternionGet(horizontalRotationSpring, horizontalRotationTarget);
    }
    public void OnVision()
    {
        playerAvatar = vision.onVisionTriggeredPlayer;
        if (GameManager.Multiplayer())
        {
            photonView.RPC("UpdatePlayerTargetRPC", RpcTarget.All, playerAvatar.photonView.ViewID);
        }
    }

    public void OnHurt()
    {
        hurtImpulse = true;
        if (emotionRoutine != null)
        {
            StopCoroutine(emotionRoutine);
        }
        emotionRoutine = StartCoroutine(HurtEmotions());
    }

    private void HurtEffect()
    {
        if (hurtImpulse)
        {
            hurtLerp += 2.5f * Time.deltaTime;
            hurtLerp = Mathf.Clamp01(hurtLerp);
            hurtableMaterial.SetFloat(hurtAmount, hurtCurve.Evaluate(hurtLerp));
            if (hurtLerp >= 1f)
            {
                    hurtLerp = 0f;
                    hurtImpulse = false;
                    hurtableMaterial.SetFloat(hurtAmount, 0f);
            }
        }
    }

    /* Emotions Logic */

    private void SetFace(Emotion emotionState, float weight)
    {
        ResetFace();

        emotion = emotionState;
        facialExpressions.SetBlendShapeWeight((int)emotionState, Mathf.Clamp(weight, 0f, 100f));

        EmotionAudio(emotionState);
    }

    private void ResetFace()
    {
        foreach (Emotion emotionState in allEmotions)
        {
            facialExpressions.SetBlendShapeWeight((int)emotionState, 0f);
        }
    }

    private Emotion IdleEmotions()
    {
        return ((Emotion[])[
            Emotion.Happy,
            Emotion.Happy2,
            Emotion.Curious,
        ])[Random.Range(0, ((Emotion[])[
            Emotion.Happy,
            Emotion.Happy2,
            Emotion.Curious,
        ]).Length)];
    }

    private Emotion SleepEmotions()
    {
        return ((Emotion[])[
            Emotion.Sleep,
            Emotion.Sleep2,
        ])[Random.Range(0, ((Emotion[])[
            Emotion.Sleep,
            Emotion.Sleep2,
        ]).Length)];
    }

    private IEnumerator GrabbedEmotions()
    {

        foreach (Emotion e in (Emotion[])[
            Emotion.Scared,
            Emotion.Sad,
            Emotion.Angry,
            Emotion.Blank,
            Emotion.Curious
        ])
        {
            SetFace(e, 100f);
            yield return new WaitForSeconds(Random.Range(3f, 4f));
        }

        SetFace(Emotion.Blank, 0f);
    }

    private IEnumerator HurtEmotions()
    {
        SetFace(Emotion.Hit, 100f);
        yield return new WaitForSeconds(0.4f);

        SetFace(Emotion.Angry, 100f);
    }

    private void EmotionAudio(Emotion emotionState)
    {
        switch (emotionState)
        {
            case Emotion.Angry:
                audioSource.PlayOneShot(angry);
                break;
            case Emotion.Curious:
                audioSource.PlayOneShot(curious);
                break;
            case Emotion.Happy:
                audioSource.PlayOneShot(happy);
                break;
            case Emotion.Happy2:
                audioSource.PlayOneShot(happy);
                break;
            case Emotion.Sleep:
                audioSource.PlayOneShot(sleep);
                break;
            case Emotion.Sleep2:
                audioSource.PlayOneShot(sleep);
                break;
            case Emotion.Sad:
                audioSource.PlayOneShot(sad);
                break;
        }
    }

    /* Networking */

    [PunRPC]
    private void UpdateStateRPC(State state)
    {
        currentState = state;
    }

    [PunRPC]
    private void UpdatePlayerTargetRPC(int viewID)
    {
        foreach (PlayerAvatar player in SemiFunc.PlayerGetList())
        {
            if (player.photonView.ViewID == viewID)
            {
                playerAvatar = player;
                break;
            }
        }
    }
}