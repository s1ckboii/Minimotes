using ExitGames.Client.Photon.StructWrapping;
using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Minimotes.Hiccubz;

public class Hiccubz : MonoBehaviour
{
    private enum State
    {
        Idle,
        Grabbed,
        Notice,
        Stashed,
        Flee 
    }

    public enum Emotion
    {
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

    private NavMeshAgent navMeshAgent;
    private EnemyVision vision;
    private PhotonView photonView;
    private PhysGrabObject physGrabObject;
    private Rigidbody rb;

    private PlayerAvatar playerAvatar;

    private State currentState;
    
    private Quaternion horizontalRotationTarget = Quaternion.identity;
    private Vector3 agentDestination;

    private bool stateImpulse;
    private bool hurtImpulse;

    private float hurtLerp;
    private float stateTimer;
    private float calmdownTimer;

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
    public Animator animator;
    public AnimationCurve hurtCurve;
    public Emotion emotion = Emotion.Happy;

    private readonly List<Material> hurtableMaterials = [];

    private static readonly Emotion[] allEmotions = (Emotion[])System.Enum.GetValues(typeof(Emotion));

    private static readonly int sitTrigger = Animator.StringToHash("idleSit");
    private static readonly int grabbedTrigger = Animator.StringToHash("grabbed");
    private static readonly int noticeTrigger = Animator.StringToHash("notice");
    private static readonly int fleeTrigger = Animator.StringToHash("runAway");
    private static readonly int sleepTrigger = Animator.StringToHash("sleeping");
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        vision = GetComponent<EnemyVision>();
        photonView = GetComponent<PhotonView>();
        physGrabObject = GetComponent<PhysGrabObject>();
        defaultLayerMask = LayerMask.GetMask("Default");
        hurtAmount = Shader.PropertyToID("_ColorOverlayAmount");
        hurtCurve = AssetManager.instance.animationCurveImpact;
        if (facialExpressions != null)
        {
            hurtableMaterials.AddRange(facialExpressions.materials);
        }
        navMeshAgent = GetComponent<NavMeshAgent>();
        StartCoroutine(WaitForNavMesh());
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
            RotationLogic();
            HurtEffect();
        }

    }

    /* States */

    private void StateIdle()
    {
        if (rb.isKinematic)
        {
            rb.isKinematic = false;
        }
        if (stateImpulse)
        {
            stateImpulse = false;
            animator.SetTrigger(sitTrigger);

            SetFace(IdleEmotions(), 100f);

            if (playerAvatar)
            {
                UpdateState(State.Notice);
            }

            if (physGrabObject.grabbed)
            {
                UpdateState(State.Grabbed);
            }
        }
    }

    private void StateGrabbed()
    {
        if (navMeshAgent.enabled && !rb.isKinematic)
        {
            navMeshAgent.enabled = false;
            rb.isKinematic = true;
        }
        if (stateImpulse)
        {
            stateImpulse = false;
            animator.SetTrigger(grabbedTrigger);

            stateTimer = 1f;
            calmdownTimer = 4f;

            if (emotionRoutine != null)
                StopCoroutine(emotionRoutine);
            emotionRoutine = StartCoroutine(GrabbedEmotions());
        }
        calmdownTimer -= Time.deltaTime;
        if (calmdownTimer <= 0f)
        {
            animator.SetTrigger(sitTrigger);
        }
        
        if (!physGrabObject.grabbed)
        {
            if (!navMeshAgent.enabled && rb.isKinematic)
            {
                navMeshAgent.enabled = true;
                rb.isKinematic = false;
            }
            if (InCartOrExtractionPoint())
            {
                UpdateState(State.Stashed);
            }
            else
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    UpdateState(State.Notice);
                }
            }            
        }
    }

    private void StateNotice()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            animator.SetTrigger(noticeTrigger);
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
            UpdateState(State.Grabbed);
        }
    }

    private void StateFlee()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            animator.SetTrigger(fleeTrigger);
            stateTimer = Random.Range(8f, 12f);

            SetFace(Emotion.Scared, 100f);

            LevelPoint levelPoint = SemiFunc.LevelPointGet(transform.position, 25f, 999f);
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
            if (stateTimer <= 0f && !physGrabObject.grabbed)
            {
                UpdateState(State.Idle);
            }

            if (physGrabObject.grabbed)
            {
                stateTimer = 0f;
                UpdateState(State.Grabbed);
            }
        }
    }

    private void StateStashed()
    {
        if (stateImpulse)
        {
            stateImpulse = false;
            animator.SetTrigger(sitTrigger);
            SetFace(Emotion.Happy, 100f);

            stateTimer = 4f;
        }        
        if (!physGrabObject.grabbed)
        {
            if (stateTimer <= 0f)
            {
                SetFace(SleepEmotions(), 100f);
                animator.SetTrigger(sleepTrigger);
            }
            if (!InCartOrExtractionPoint())
            {
                UpdateState(State.Notice);
            }
        }
        else if (physGrabObject.grabbed)
        {
            stateTimer = 0f;
            UpdateState(State.Grabbed);
        }
    }
    private void UpdateState(State state)
    {
        if (currentState != state)
        {
            currentState = state;
            stateImpulse = true;
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
    }

    /* Extra Logic */

    private IEnumerator WaitForNavMesh()
    {
        yield return new WaitForSeconds(2f);
        if (NavMesh.SamplePosition(base.transform.position, out var navHit, 20f, -1))
        {
            base.transform.position = navHit.position;
            WaitForPos();
        }
    }

    private IEnumerator WaitForPos()
    {
        yield return new WaitForSeconds(2f);
        if (NavMesh.SamplePosition(base.transform.position, out var navHit, 20f, -1))
        {
            navMeshAgent.enabled = true;
            navMeshAgent.Warp(navHit.position);
        }
    }

    private bool InCartOrExtractionPoint()
    {
        return RoundDirector.instance.dollarHaulList.Contains(gameObject);
    }

    private void RotationLogic()
    {
        if (navMeshAgent.velocity.normalized.magnitude > 0.1f)
        {
            horizontalRotationTarget = Quaternion.LookRotation(navMeshAgent.velocity.normalized);
            horizontalRotationTarget.eulerAngles = new Vector3(0f, horizontalRotationTarget.eulerAngles.y, 0f);
            horizontalRotationSpring.speed = 15f;
            horizontalRotationSpring.damping = 0.8f;
        }
        transform.rotation = SemiFunc.SpringQuaternionGet(horizontalRotationSpring, horizontalRotationTarget);
    }

    public void OnVision()
    {
        if (SemiFunc.IsMasterClientOrSingleplayer())
        {
            playerAvatar = vision.onVisionTriggeredPlayer;
            if (GameManager.Multiplayer())
            {
                photonView.RPC("UpdatePlayerTargetRPC", RpcTarget.All, playerAvatar.photonView.ViewID);
            }
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
            foreach (var hurtableMaterial in hurtableMaterials)
            {
                hurtableMaterial.SetFloat(hurtAmount, hurtCurve.Evaluate(hurtLerp));
                if (hurtLerp >= 1f)
                {
                    hurtLerp = 0f;
                    hurtImpulse = false;
                    hurtableMaterial.SetFloat(hurtAmount, 0f);
                }
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
        Emotion[] idleOptions =
        {
            Emotion.Happy,
            Emotion.Happy2,
            Emotion.Curious
        };

        return idleOptions[Random.Range(0, idleOptions.Length)];
    }

    private Emotion SleepEmotions()
    {
        Emotion[] sleepOptions =
{
            Emotion.Sleep,
            Emotion.Sleep2,
            Emotion.Happy,
            Emotion.Happy2
        };

        return sleepOptions[Random.Range(0, sleepOptions.Length)];
    }

    private IEnumerator GrabbedEmotions()
    {

        foreach (Emotion e in (Emotion[])[
            Emotion.Scared,
            Emotion.Sad,
            Emotion.Angry,
            Emotion.Curious,
            Emotion.Happy
        ])
        {
            SetFace(e, 100f);
            yield return new WaitForSeconds(Random.Range(3f, 4f));
        }

        SetFace(Emotion.Happy, 0f);
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