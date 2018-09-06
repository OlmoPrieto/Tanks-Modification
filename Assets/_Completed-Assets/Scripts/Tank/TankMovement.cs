using UnityEngine;
using UnityEngine.Assertions;
using System.Collections;

namespace Complete
{
    public class TankMovement : MonoBehaviour
    {
        public int m_PlayerNumber = 1;              // Used to identify which tank belongs to which player.  This is set by this tank's manager.
        public float m_Speed = 12f;                 // How fast the tank moves forward and back.
        public float m_TurnSpeed = 180f;            // How fast the tank turns in degrees per second.
        public AudioSource m_MovementAudio;         // Reference to the audio source used to play engine sounds. NB: different to the shooting audio source.
        public AudioClip m_EngineIdling;            // Audio to play when the tank isn't moving.
        public AudioClip m_EngineDriving;           // Audio to play when the tank is moving.
		public float m_PitchRange = 0.2f;           // The amount by which the pitch of the engine noises can vary.
        public CameraControl m_CameraControl;       // Because the tank and the camera's moving are related, store a reference
        public bool m_InBounds = false;
        public float m_TimeToTurnAI = 5.0f;         // If no input for 5 seconds, turn into AI


        private string m_MovementAxisName;          // The name of the input axis for moving forward and back.
        private string m_TurnAxisName;              // The name of the input axis for turning.
        private Rigidbody m_Rigidbody;              // Reference used to move the tank.
        private float m_MovementInputValue;         // The current value of the movement input.
        private float m_TurnInputValue;             // The current value of the turn input.
        private float m_OriginalPitch;              // The pitch of the audio source at the start of the scene.
        private ParticleSystem[] m_particleSystems; // References to all the particles systems used by the Tanks
        private Camera m_Camera;                    // Quick access to the CameraControl's camera
        private float m_AITimer = 0.0f;
        private bool m_UseSmoothedInput = true;
        private TankBehaviour m_Behaviour;

        private void Awake ()
        {
            m_Rigidbody = GetComponent<Rigidbody> ();

            // Because a Prefab is used to instantiate tanks, editor refs cannot
            // be set manually, so find the CameraControl script. Because it should only
            // be one in the scene, this function works.
            m_CameraControl = Object.FindObjectOfType<CameraControl>();

            m_Behaviour = GetComponent<TankBehaviour>();
        }


        private void OnEnable ()
        {
            // When the tank is turned on, make sure it's not kinematic.
            m_Rigidbody.isKinematic = false;

            // Also reset the input values.
            m_MovementInputValue = 0f;
            m_TurnInputValue = 0f;

            // We grab all the Particle systems child of that Tank to be able to Stop/Play them on Deactivate/Activate
            // It is needed because we move the Tank when spawning it, and if the Particle System is playing while we do that
            // it "think" it move from (0,0,0) to the spawn point, creating a huge trail of smoke
            m_particleSystems = GetComponentsInChildren<ParticleSystem>();
            for (int i = 0; i < m_particleSystems.Length; ++i)
            {
                m_particleSystems[i].Play();
            }

            Assert.IsNotNull(m_CameraControl, "CameraControl not set");
            m_Camera = m_CameraControl.m_Camera;

            Assert.IsNotNull(m_Camera, "Error! Camera not set");

            m_InBounds = true;
            m_UseSmoothedInput = true;
            m_AITimer = 0.0f;
        }


        private void OnDisable ()
        {
            // When the tank is turned off, set it to kinematic so it stops moving.
            m_Rigidbody.isKinematic = true;

            // Stop all particle system so it "reset" it's position to the actual one instead of thinking we moved when spawning
            for(int i = 0; i < m_particleSystems.Length; ++i)
            {
                m_particleSystems[i].Stop();
            }
        }


        private void Start ()
        {
            // The axes names are based on player number.
            m_MovementAxisName = "Vertical" + m_PlayerNumber;
            m_TurnAxisName = "Horizontal" + m_PlayerNumber;

            // Store the original pitch of the audio source.
            m_OriginalPitch = m_MovementAudio.pitch;
        }


        private void Update ()
        {
            m_AITimer += Time.deltaTime;

            // Store the value of both input axes.
            if (m_UseSmoothedInput)
            {
                m_MovementInputValue = Input.GetAxis(m_MovementAxisName);
            }
            else
            {
                // This raw input is used for when the tanks reaches the bounds 
                // of the camera, so they don't exit it bit by bit when the camera
                // repositions afterward.
                m_MovementInputValue = Input.GetAxisRaw(m_MovementAxisName);
            }
            m_TurnInputValue = Input.GetAxis(m_TurnAxisName);


            if (m_MovementInputValue != 0.0f || m_TurnInputValue != 0.0f)
            {
                m_AITimer = 0.0f;
                m_Behaviour.DisableAI();
            }

            if (!m_Behaviour.AIEnabled() && m_AITimer >= m_TimeToTurnAI)
            {
                m_Behaviour.EnableAI();
            }

            EngineAudio ();
        }


        private void EngineAudio ()
        {
            // If there is no input (the tank is stationary)...
            if (Mathf.Abs (m_MovementInputValue) < 0.1f && Mathf.Abs (m_TurnInputValue) < 0.1f)
            {
                // ... and if the audio source is currently playing the driving clip...
                if (m_MovementAudio.clip == m_EngineDriving)
                {
                    // ... change the clip to idling and play it.
                    m_MovementAudio.clip = m_EngineIdling;
                    m_MovementAudio.pitch = Random.Range (m_OriginalPitch - m_PitchRange, m_OriginalPitch + m_PitchRange);
                    m_MovementAudio.Play ();
                }
            }
            else
            {
                // Otherwise if the tank is moving and if the idling clip is currently playing...
                if (m_MovementAudio.clip == m_EngineIdling)
                {
                    // ... change the clip to driving and play.
                    m_MovementAudio.clip = m_EngineDriving;
                    m_MovementAudio.pitch = Random.Range(m_OriginalPitch - m_PitchRange, m_OriginalPitch + m_PitchRange);
                    m_MovementAudio.Play();
                }
            }
        }


        private void FixedUpdate ()
        {
            // Adjust the rigidbodies position and orientation in FixedUpdate.
            Move ();
            Turn ();
        }


        private void Move ()
        {
            // Create a vector in the direction the tank is facing with a magnitude based on the input, speed and the time between frames.
            Vector3 movement = transform.forward * m_MovementInputValue * m_Speed * Time.deltaTime;
            
            Vector3 new_position = m_Rigidbody.position + movement;
            Vector3 vp_coords = m_Camera.WorldToViewportPoint(new_position);

            // Not using 0.0f and 1.0f because the tank would be half visible.
            if ((vp_coords[0] > 0.04f && vp_coords[0] < 0.96f) && 
                (vp_coords[1] > 0.04f && vp_coords[1] < 0.96f))
            {
                if (!m_Behaviour.AIEnabled())
                {
                    m_Rigidbody.MovePosition(new_position);
                }
                // If it came from outside bounds, give a brief delay until GetInput can be used again
                if (m_InBounds == false)
                {
                    StartCoroutine(ResetInputMode());
                }
                m_InBounds = true;
            }
            else 
            {
                if (!m_Behaviour.AIEnabled())
                {
                    m_Rigidbody.MovePosition(m_Rigidbody.position);
                }
                m_InBounds = false;
                m_UseSmoothedInput = false;
            }
        }


        public void Turn (float amount = 0.0f)
        {
            amount = Mathf.Clamp(amount, -1.0f, 1.0f);
            if (amount != 0.0f)
            {
                m_TurnInputValue = amount;
            }

            float turn = m_TurnInputValue * m_TurnSpeed * Time.deltaTime;

            // Make this into a rotation in the y axis.
            Quaternion turnRotation = Quaternion.Euler (0f, turn, 0f);

            // Apply this rotation to the rigidbody's rotation.
            m_Rigidbody.MoveRotation (m_Rigidbody.rotation * turnRotation);
        }

        // Used to reset the input flag when the tank comes from reaching the camera's bounds.
        private IEnumerator ResetInputMode()
        {
            yield return new WaitForSeconds(0.3f);
            m_UseSmoothedInput = true;
        }

        public Vector3 GetPosition()
        {
            return m_Rigidbody.position;
        }
    }
}