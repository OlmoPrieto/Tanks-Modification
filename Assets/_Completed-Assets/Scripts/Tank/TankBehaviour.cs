using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Assertions;

namespace Complete
{
  public class TankBehaviour : MonoBehaviour
  {

    enum State {
      Disabled = 0,
      Moving,
      Scanning,
      TurningToEnemy,
    };


    public float m_MaxTimeTurning = 1.5f;                 // Seconds.
    public float m_NormalizedTurningSpeed = 0.25f;        // Used when turning in Scanning State.
    public float m_VisionConeAmplitude = 90.0f;           // 90 degrees in total, 45 degrees at each side of the forward vector.
    public float m_MinAngleToFaceEnemy = 5.0f;            // When facing the enemy, if it is 30 degrees at each side of the player's forward, you can start chasing it.
    public float m_TargetStopDistance = 20.0f;            // Distance at which the AI will stop when approaching its target.
    public float m_EnemyStopDistance = 15.0f;
    public float m_MaxRandomDistance = 100.0f;            // Max distance from the tank's position to calculate a random position in the NavMesh.
    public float m_MaxReloadTime = 1.5f;
    [HideInInspector] public TankMovement m_TankMovement;
    [HideInInspector] public TankShooting m_TankShooting;
    [HideInInspector] public GameObject m_Enemy;
    [HideInInspector] public int m_PlayerNumber;

    private NavMeshAgent m_NVA;
    private State m_State = State.Disabled;
    private Vector3 m_TargetPoint;
    private Rigidbody m_Rigidbody;
    private float m_TimeTurning = 0.0f;
    private bool m_TurnedRight = false;
    private bool m_CanShoot = true;
    private float m_CurrentReloadTime = 0.0f;
    private bool m_GettingBackFromEnemy = false;

  	void Awake()
    {
      m_Rigidbody = GetComponent<Rigidbody>();

		  m_TankMovement = GetComponent<TankMovement>();
      m_TankShooting = GetComponent<TankShooting>();

      m_NVA = GetComponent<NavMeshAgent>();
      Assert.IsNotNull(m_NVA, "NavMeshAgent not found!");
      m_NVA.enabled = false;

      // Set NavMeshAgent movement and turning speed;
      m_NVA.speed = m_TankMovement.m_Speed;
      m_NVA.angularSpeed = m_TankMovement.m_TurnSpeed;
      m_NVA.acceleration = 16.0f;

      m_State = State.Disabled;
  	}
  	
  	void Update()
    {
      if (m_NVA.enabled)
      {
        // Check reload timer (can be checked at any state).
        m_CurrentReloadTime += Time.deltaTime;
        if (m_CurrentReloadTime >= m_MaxReloadTime)
        {
          m_CurrentReloadTime = 0.0f;
          m_CanShoot = true;
        }

        // Check FSM
    		switch (m_State)
        {
          case State.Disabled:
          {
            break;
          }

          case State.Moving:
          {
            // If the tank isn't in bounds, pick a random point in the NavMesh and test if
            // it is inside camera view.
            if (!m_TankMovement.m_InBounds)
            {
              StopMoving();
              Vector3 v = Vector3.zero;
              do
              {
                PickTargetInNavMesh();
                v = Camera.main.WorldToViewportPoint(m_TargetPoint);
              }
              while ((v[0] < 0.04f || v[0] > 0.96f) || 
                     (v[1] < 0.04f || v[1] > 0.96f));

              // Go to the next Update so the tank prioritizes going in bounds.
              break;
            }

            // If at any point the path for the NavMeshAgent is not reachable, find a new target.
            if (m_NVA.pathStatus == NavMeshPathStatus.PathPartial || 
              m_NVA.pathStatus == NavMeshPathStatus.PathInvalid)
            {
              PickTargetInNavMesh();
            }

            bool enemy_in_sight = false;

            Vector3 enemy_pos = m_Enemy.GetComponent<TankMovement>().GetPosition();
            Vector3 direction = enemy_pos - m_Rigidbody.position;

            // Check if the enemy is within the view cone
            float angle = Vector3.Angle(transform.forward, direction);
            if (Mathf.Abs(angle) <= m_VisionConeAmplitude * 0.5f)
            {
              // If it is, check if there is an obstacle between this tank and the enemy.
              RaycastHit hit;
              if (Physics.Raycast(m_Rigidbody.position, direction, out hit))
              {
                if (hit.collider.gameObject == m_Enemy)
                {
                  // Enemy in sight, set a boolean to be used in the code down below.
                  enemy_in_sight = true;
                  // If you are not fleeing from the enemy, mark its position as your target.
                  if (!m_GettingBackFromEnemy)
                  {
                    m_TargetPoint = enemy_pos;
                    m_NVA.isStopped = false;
                    m_NVA.SetDestination(enemy_pos);
                  }
                }
                else 
                {
                  // If you hit something else in the way (i.e. a wall) and it's close, 
                  // don't allow to shoot as it can explode in your face
                  if ((hit.transform.position - m_Rigidbody.position).sqrMagnitude < 
                    m_TargetStopDistance * m_TargetStopDistance * 0.25f)
                  {
                    m_CanShoot = false;
                    m_CurrentReloadTime = 0.0f;
                  }
                }
              }
            }

            // Check distances between you and the enemy and target position.
            float sqr_distance = (m_TargetPoint - m_Rigidbody.position).sqrMagnitude;
            float sqr_enemy_distance = (enemy_pos - m_Rigidbody.position).sqrMagnitude;
            bool target_close = sqr_distance <= m_TargetStopDistance * m_TargetStopDistance;
            bool enemy_close = sqr_enemy_distance <= m_EnemyStopDistance * m_EnemyStopDistance;

            if (!enemy_close)
            {
              m_GettingBackFromEnemy = false;
            }

            if (enemy_in_sight)
            {
              // By default, use half the maximum launch force.
              float force = m_TankShooting.m_MaxLaunchForce * 0.5f;

              if (!enemy_close)
              {
                // Because you are far away from the enemy, you can resume chasing it (if you where fleeing).
                m_GettingBackFromEnemy = false;
                m_TargetPoint = enemy_pos;
                m_NVA.isStopped = false;
                m_NVA.SetDestination(enemy_pos);

                // If the enemy isn't close but quite near the stopping distance, shoot.
                if (m_CanShoot && sqr_enemy_distance <= m_EnemyStopDistance * m_EnemyStopDistance * 4)
                {
                  force += m_TankShooting.m_MinLaunchForce * 0.4f;

                  m_TankShooting.Fire(force);

                  m_CanShoot = false;
                  m_CurrentReloadTime = 0.0f;
                }
              }
              else  // If the enemy is in sight but you are close to it, retreat a little / flee.
              {
                m_GettingBackFromEnemy = true;
                m_NVA.ResetPath();
                m_NVA.SetDestination(m_Rigidbody.position - direction.normalized * 10.0f);

                // Shoot with little force as the enemy is close.
                force = m_TankShooting.m_MinLaunchForce * 0.725f;
              
                if (m_CanShoot)
                {
                  m_TankShooting.Fire(force);

                  m_CanShoot = false;
                  m_CurrentReloadTime = 0.0f;
                }
              }
            }
            else 
            {
              // If the enemy wasn't seen and you are close to it, turn.
              // At this point you could be fleeing from the enemy, so you will turn to it 
              // while fleeing.
              if (enemy_close)
              {
                m_Rigidbody.velocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
                StartTurningToEnemyState();
              }
            }

            // If you are close to the target destination pick a new one.
            if (target_close)
            {
              PickTargetInNavMesh();
            }

            break;
          }

          case State.Scanning:
          {
            // When in Scanning State, you first turn right the m_TurnAngle degrees and
            // then you turn left m_TurnAngle * 2.0f degrees. Meanwhile, detect where
            // the player is and throw raycasts to see if you can chase it or shoot it.

            // Increase counting time
            m_TimeTurning += Time.deltaTime;

            // In the first third of the m_MaxTimeTurning, turn right.
            // If that time is surpased, start turning left.
            if (m_TimeTurning >= m_MaxTimeTurning * 0.5f)
            {
              if (!m_TurnedRight)
              {
                m_TurnedRight = true;
              }
              
              // Here you can reuse code to check if you finished turning.
              if (m_TimeTurning >= m_MaxTimeTurning)
              {
                // At this point the enemy hasn't been spotted, so select a random point in the NavMesh
                PickTargetInNavMesh();
                StartMovingState();

                break;
              }
            }

            if (!m_TurnedRight)
            {
              m_TankMovement.Turn(m_NormalizedTurningSpeed);  // Turn right
            }
            else
            {
              m_TankMovement.Turn(-m_NormalizedTurningSpeed); // Turn left 
            }

            // Check where the enemy is
            Vector3 enemy_pos = m_Enemy.GetComponent<TankMovement>().GetPosition();

            Vector3 direction = enemy_pos - m_Rigidbody.position;
            float angle = Vector3.Angle(transform.forward, direction);  // This could be done performing a dot operation too
            if (Mathf.Abs(angle) <= m_VisionConeAmplitude * 0.5f)
            {
              RaycastHit hit;
              if (Physics.Raycast(m_Rigidbody.position, direction, out hit))
              {
                if (hit.collider.gameObject == m_Enemy)
                {
                  // Enemy in sight
                  StartTurningToEnemyState();
                }
              }
            }

            break;
          }

          case State.TurningToEnemy:
          {
            Vector3 enemy_pos = m_Enemy.GetComponent<TankMovement>().GetPosition();
            Vector3 direction = enemy_pos - m_Rigidbody.position;

            // If you are almost facing the enemy, go to it.
            float angle = Vector3.Angle(transform.forward, direction);
            if (Mathf.Abs(angle) <= m_MinAngleToFaceEnemy * 0.5f)
            {
              m_TargetPoint = enemy_pos;
              m_NVA.isStopped = false;
              m_NVA.SetDestination(enemy_pos);
              StartMovingState();
            }
            else 
            {
              // Keep turning until facing the enemy (checked above)
              m_TankMovement.Turn(angle);
            }

            break;
          }
        }
      } // if NavMeshAgent is enabled
  	}

    private void StartScanningState()
    {
      m_State = State.Scanning;
      m_TurnedRight = false;
      StopMoving();
      m_TimeTurning = 0.0f; // Reset timer
    }

    private void StartMovingState()
    {
      m_State = State.Moving;
    }

    private void StartTurningToEnemyState()
    {
      m_State = State.TurningToEnemy;
    }

    private void PickTargetInNavMesh()
    {
      Vector3 random_direction = Random.insideUnitSphere * m_MaxRandomDistance;
      random_direction += m_Rigidbody.position;

      NavMeshHit hit;
      NavMesh.SamplePosition(random_direction, out hit, m_MaxRandomDistance, 1);
      if (hit.hit)
      {
        m_TargetPoint = hit.position;
        m_NVA.isStopped = false;
        m_NVA.SetDestination(m_TargetPoint);
      }
    }

    public void StopMoving()
    {
      m_NVA.isStopped = true;
      m_NVA.ResetPath();
      m_Rigidbody.velocity = Vector3.zero;
      m_Rigidbody.angularVelocity = Vector3.zero;
    }

    public void EnableAI()
    {
      m_NVA.enabled = true;
      m_CurrentReloadTime = 0.0f;
      m_CanShoot = false;
      StartScanningState();
    }

    public void DisableAI()
    {
      m_State = State.Disabled;
      m_NVA.enabled = false;
      StopMoving();
    }

    public bool AIEnabled()
    {
      return m_NVA.enabled;
    }
  }
} // namespace
