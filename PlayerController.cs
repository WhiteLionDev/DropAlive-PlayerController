using UnityEngine;
using Rewired;
using Com.LuisPedroFonseca.ProCamera2D;
using System.Collections;
using Steamworks;

public class PlayerController : MonoBehaviour
{
	public enum AnalyticState
	{
		Won,
		Died,
		Quit
	}

    private const float SkinWidth = 0.04f;
    private bool _isFacingRight = true;

    public bool handleInput;
    [HideInInspector] public bool avoidAction;

    public LayerMask whatIsPlatform;
    public LayerMask whatIsClimbable;

    [HideInInspector] public bool isGrounded;
    [HideInInspector] public bool isOnWall;
    [HideInInspector] public bool isBackOnWall;

    private int jumpCount = 0;

    private bool isOnClimbable;
    private bool isClimbing = false;
    private bool isClimbingNow = false;

    private float time;

    private ControllerParameters _defoultParameters; 

    public ControllerParameters parameters
    {
        get { return _overrideParameters ?? _defoultParameters; }
        set { _defoultParameters = value; }

    }
    public PlayerSounds Sounds { get { return _sounds; } }

    public bool IsDead { get; private set; }

    public float scaleRatio
    {
        get { return IsDead ? 0 : (transform.localScale.x - parameters.minScale) / (1 - parameters.minScale) ; }
    }

    private float _volIni = 0;
    public float GetTotalVolumen()
    {
        return ((this.transform.localScale.x * 100) / _volIni);
    }

    public StateChanger.State currentState
    {
        get { return (this.GetComponent<StateChanger>().currentState); }
    }

    public bool CanJump
    {
        get
        {
            if (parameters.jumpRestrictions == ControllerParameters.JumpBehavior.CanJumpAnywhere)
                return _jumpIn <= 0;

            if (parameters.jumpRestrictions == ControllerParameters.JumpBehavior.CanJumpOnGroundAndWall)
                return isGrounded || isOnClimbable;

            if (parameters.jumpRestrictions == ControllerParameters.JumpBehavior.CanJumpOnGround)
                return isGrounded;

            return false;
        }
    }

    private float _movementFactor;
    private float _jumpIn;
    private float _initialDropSpeed;
    private StateChanger.State _initialState;
    private ControllerParameters _overrideParameters;
    private Rigidbody2D _rigidbody;
    private CircleCollider2D _collider;
    private PlayerSounds _sounds;
    private StateChanger _stateChanger;
    private Animator anim;

    private float _rayDistance;
    private Vector3 _rayOrigin;
    private Vector3 _rayDestiny;

    private float _xPushForce;
    private float _yPushForce;
    private Vector2 _pushForce = Vector2.zero;
    //private bool quiet;
	private StateSpecificVector2 _pushEvent;

    private float moving;
    private float maxAltitud = float.MinValue;
    private float minAltitud = float.MaxValue;
    private bool falling;
    private RaycastHit2D raycastHit;
    private string currentPlatformTag;
    [HideInInspector] public bool crashing;

    private bool isShuttingDown;

    private float walljumpCD  = 0;
    public float wallJumpDelay = 0.2f;
    public ParticleSystem warningParticles;
    private ParticleSystem.EmissionModule emmiter;

    private bool soundFxAllowed;

    void Start ()
    {
        _rigidbody = this.GetComponent<Rigidbody2D>();
        _collider = this.GetComponent<CircleCollider2D>();
        _sounds = this.GetComponent<PlayerSounds>();
        _stateChanger = this.GetComponent<StateChanger>();
        _volIni = transform.localScale.x;
        _initialDropSpeed = _stateChanger.dropParameters.maxSpeed.x;
        _initialState = currentState;
        anim = this.GetComponent<Animator>();
        ProCamera2D.Instance.RemoveAllCameraTargets();
        ProCamera2D.Instance.AddCameraTarget(this.transform);
		_pushEvent = new StateSpecificVector2(AffectedStates.Cloud, Vector2.zero);
        InputProcesor.Instance.MyPlayer.AddInputEventDelegate(ActionJustPressed, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, RewiredActions.Action);
        InputProcesor.Instance.MyPlayer.AddInputEventDelegate(Pause, UpdateLoopType.Update, InputActionEventType.ButtonJustPressed, RewiredActions.Pause);
        _isFacingRight = !(transform.localRotation.y > 0);
        soundFxAllowed = true;

        if(warningParticles != null)
        {
            emmiter = warningParticles.emission;
            warningParticles.Play();
        }

    }

    void FixedUpdate()
    {
        EmitRays();
    }

    void Update ()
    {
        if (GameManager.Instance.CurrentGameState == GameManager.GameState.Playing)
        {
            Move();

            switch (currentState)
            {
                case StateChanger.State.Drop:
                    ManageDrop();
                    break;
                case StateChanger.State.Cloud:
                    ManageCloud();
                    break;
                case StateChanger.State.Ice:
                    ManageIce();
                    break;
            }
            _jumpIn -= Time.deltaTime;
        }
        if (_pushForce.x > 0)
            _pushForce.x = Mathf.Clamp(_pushForce.x - (Time.deltaTime * 10), 0, _pushForce.x);
        else
            _pushForce.x = Mathf.Clamp(_pushForce.x + (Time.deltaTime * 10), _pushForce.x, 0);

        if (_pushForce.y > 0)
            _pushForce.y = Mathf.Clamp(_pushForce.y - (Time.deltaTime * 10), 0, _pushForce.y);
        else
            _pushForce.y = Mathf.Clamp(_pushForce.y + (Time.deltaTime * 10), _pushForce.y, 0);
    }

    void LateUpdate()
    {
        if(currentState == StateChanger.State.Ice)
            anim.SetBool("Crashing", false);
    }

    private void EmitRays()
    {
        _rayDistance = (_collider.radius + SkinWidth) * transform.localScale.x;
        _rayOrigin = _collider.bounds.center - new Vector3((_collider.radius / 1.4f) * transform.localScale.x, _rayDistance, 0);
        _rayDestiny = _collider.bounds.center - new Vector3(-1 * (_collider.radius / 1.4f) * transform.localScale.x, _rayDistance, 0);

		//Debug.DrawLine(_rayOrigin, _rayDestiny, Color.green);
        raycastHit = Physics2D.Linecast(_rayOrigin, _rayDestiny, whatIsPlatform);
        if (raycastHit)
        {
            isGrounded = true;
            currentPlatformTag = raycastHit.collider.gameObject.tag;
        }
        else
        {
            isGrounded = false;
            currentPlatformTag = string.Empty;
        }

        if (_isFacingRight)
        {
            _rayOrigin = _collider.bounds.center - new Vector3(_rayDistance, (_collider.radius / 2.5f) * transform.localScale.y, 0);
            _rayDestiny = _collider.bounds.center + new Vector3(-1 * _rayDistance, (_collider.radius / 2) * transform.localScale.y, 0);

            //Debug.DrawLine(_rayOrigin, _rayDestiny, Color.red);
            isBackOnWall = Physics2D.Linecast(_rayOrigin, _rayDestiny, whatIsPlatform);

            _rayOrigin = _collider.bounds.center + new Vector3(_rayDistance, -1 * (_collider.radius / 2.5f) * transform.localScale.y, 0);
            _rayDestiny = _collider.bounds.center + new Vector3(_rayDistance, (_collider.radius / 2) * transform.localScale.y, 0);

            //Debug.DrawLine(_rayOrigin, _rayDestiny, Color.blue);
            isOnWall = Physics2D.Linecast(_rayOrigin, _rayDestiny, whatIsPlatform);
        }
        else
        {
            _rayOrigin = _collider.bounds.center + new Vector3(_rayDistance, -1 * (_collider.radius / 2.5f) * transform.localScale.y, 0);
            _rayDestiny = _collider.bounds.center + new Vector3(_rayDistance, (_collider.radius / 2) * transform.localScale.y, 0);

            //Debug.DrawLine(_rayOrigin, _rayDestiny, Color.red);
            isBackOnWall = Physics2D.Linecast(_rayOrigin, _rayDestiny, whatIsPlatform);

            _rayOrigin = _collider.bounds.center - new Vector3(_rayDistance, (_collider.radius / 2.5f) * transform.localScale.y, 0);
            _rayDestiny = _collider.bounds.center + new Vector3(-1 * _rayDistance, (_collider.radius / 2) * transform.localScale.y, 0);

            //Debug.DrawLine(_rayOrigin, _rayDestiny, Color.blue);
            isOnWall = Physics2D.Linecast(_rayOrigin, _rayDestiny, whatIsPlatform);
        }

        if (currentState == StateChanger.State.Drop)
        {
            if (!isGrounded && _movementFactor != 0)
            {
                if (_isFacingRight)
                {
                    //Debug.DrawLine(_collider.bounds.center, new Vector3(_rayDestiny.x + 0.05f, _collider.bounds.center.y, 0), Color.red);
                    isOnClimbable = Physics2D.Linecast(_collider.bounds.center, new Vector3(_rayDestiny.x + 0.05f, _collider.bounds.center.y, 0), whatIsClimbable);
                }
                else
                {
                    //Debug.DrawLine(_collider.bounds.center, new Vector3(_rayDestiny.x - 0.05f, _collider.bounds.center.y, 0), Color.red);
                    isOnClimbable = Physics2D.Linecast(_collider.bounds.center, new Vector3(_rayDestiny.x - 0.05f, _collider.bounds.center.y, 0), whatIsClimbable);
                }

            }
            else
            {
                isOnClimbable = false;
            }

            if (isClimbingNow)
            {
                if (_isFacingRight)
                {
                    //Debug.DrawLine(_collider.bounds.center, new Vector3(_rayDestiny.x + 0.2f, _collider.bounds.center.y, 0), Color.magenta);
                    isClimbing = Physics2D.Linecast(_collider.bounds.center, new Vector3(_rayDestiny.x + 0.2f, _collider.bounds.center.y, 0), whatIsClimbable);
                }
                else
                {
                    //Debug.DrawLine(_collider.bounds.center, new Vector3(_rayDestiny.x - 0.2f, _collider.bounds.center.y, 0), Color.magenta);
                    isClimbing = Physics2D.Linecast(_collider.bounds.center, new Vector3(_rayDestiny.x - 0.2f, _collider.bounds.center.y, 0), whatIsClimbable);
                }

                if (isClimbing && (this._rigidbody.velocity.y < -0.01f || Mathf.Abs(this._rigidbody.velocity.x) < -0.01f))
                    isClimbing = false;
                if (!isClimbing)
                    isClimbingNow = false;
            }
        }
    }

    private void ManageDrop()
    {
        anim.SetBool("Ground", isGrounded);
        anim.SetFloat("vSpeed", _rigidbody.velocity.y);
        anim.SetFloat("Speed", Mathf.Abs(_movementFactor));

        if (_movementFactor > 0 && !_isFacingRight && walljumpCD <= 0)
        {
            _isFacingRight = !_isFacingRight;
            transform.eulerAngles = Vector2.zero;
        }
        else if (_movementFactor < 0 && _isFacingRight && walljumpCD <= 0)
        {
            _isFacingRight = !_isFacingRight;
            transform.eulerAngles = new Vector2(0f, 180f);
        }

        if (isOnClimbable && ((_isFacingRight && _movementFactor > 0) || (!_isFacingRight && _movementFactor < 0)))
        {
            walljumpCD = wallJumpDelay;
            if (_rigidbody.velocity.y < 0.01f)
            {
                _rigidbody.velocity = new Vector2(_rigidbody.velocity.x, Mathf.Clamp(_rigidbody.velocity.y, -0.5f, 0));
                anim.SetBool("Climb", true);
            }
        }
        else if (walljumpCD > 0 && isOnClimbable)
        {
            walljumpCD -= Time.deltaTime;
            if (_rigidbody.velocity.y < 0.01f)
            {
                _rigidbody.velocity = new Vector2(_rigidbody.velocity.x, Mathf.Clamp(_rigidbody.velocity.y, -0.5f, 0));
                anim.SetBool("Climb", true);
            }
        }
        else
        {
            walljumpCD = 0;
            anim.SetBool("Climb", false);
        }

        if (transform.localScale.x <= parameters.minScale && !IsDead)
        {
			Kill(AffectedStates.Drop | AffectedStates.Ice | AffectedStates.Cloud, DamageSources.Movement);
        }
    }

    private void ManageCloud()
    {
        anim.SetFloat("vSpeed", _rigidbody.velocity.y);
        anim.SetBool("stoped", !handleInput);
        anim.SetFloat("Speed", Mathf.Abs(_movementFactor));

        if (_movementFactor > 0 && !_isFacingRight)
        {
            _isFacingRight = !_isFacingRight;
            transform.eulerAngles = Vector2.zero;
        }
        else if (_movementFactor < 0 && _isFacingRight)
        {
            _isFacingRight = !_isFacingRight;
            transform.eulerAngles = new Vector2(0f, 180f);
        }

        _rigidbody.velocity = new Vector2(_rigidbody.velocity.x, parameters.maxSpeed.y + _pushForce.y);
        transform.localScale -= new Vector3(Mathf.Abs(parameters.downScaleValor), Mathf.Abs(parameters.downScaleValor), 0) * Time.deltaTime;

        if (transform.localScale.x <= parameters.minScale && !IsDead)
        {
			Kill(AffectedStates.Drop | AffectedStates.Ice | AffectedStates.Cloud, DamageSources.Movement);
        }
    }

    private void ManageIce()
    {
        anim.SetBool("Ground", isGrounded);

        if (moving > 0 && !_isFacingRight)
        {
            _isFacingRight = !_isFacingRight;
            transform.eulerAngles = new Vector2(0f, 0f);
        }
        else if (moving < 0 && _isFacingRight)
        {
            _isFacingRight = !_isFacingRight;
            transform.eulerAngles = new Vector2(0f, 180f);
        }

        anim.SetFloat("vSpeed", _rigidbody.velocity.y);

        if (_movementFactor > 0 && !(isOnWall && _isFacingRight))
        {
            if (moving < 0)
            {
                moving = Mathf.Clamp(moving + (parameters.inertiaBrakes * Time.deltaTime), -1, 1);
                anim.SetBool("Braking", true);
            }
            else
            {
                anim.SetBool("Braking", false);
            }
            moving = Mathf.Clamp(moving + (parameters.acceleration * Time.deltaTime), -1, 1);
        }
        else if (_movementFactor < 0 && !(isOnWall && !_isFacingRight))
        {
            if (moving > 0)
            {
                moving = Mathf.Clamp(moving - (parameters.inertiaBrakes * Time.deltaTime), -1, 1);
                anim.SetBool("Braking", true);
            }
            else
            {
                anim.SetBool("Braking", false);
            }
            moving = Mathf.Clamp(moving - (parameters.acceleration * Time.deltaTime), -1, 1);
        }
        else
        {
            if (moving > 0)
                moving = Mathf.Clamp(moving - (parameters.inertia * Time.deltaTime), 0, 1);
            else if (moving < 0)
                moving = Mathf.Clamp(moving + (parameters.inertia * Time.deltaTime), -1, 0);
        }

        anim.SetFloat("Speed", Mathf.Abs(moving));
        if (!isOnWall && !IsDead)
        {
            _rigidbody.velocity = new Vector2(moving * parameters.maxSpeed.x, Mathf.Clamp(_rigidbody.velocity.y, -7, 8));
        }
        else if (_rigidbody.velocity.y < -0.01f && !isGrounded && ((_isFacingRight && _movementFactor > 0) || (!_isFacingRight && _movementFactor < 0)))
        {
            _rigidbody.velocity = new Vector2(0, Mathf.Clamp(_rigidbody.velocity.y, -2, 0));
            maxAltitud = float.MinValue;
            minAltitud = float.MaxValue;
            falling = false;
        }
    }

    public void ResetVariables()
    {
        moving = 0;
        maxAltitud = float.MinValue;
        minAltitud = float.MaxValue;
        falling = false;
        crashing = false;
        isClimbing = false;
        isClimbingNow = false;
    }

    private void Move()
    {
        if (handleInput)
            _movementFactor = InputProcesor.Instance.MyPlayer.GetAxis(RewiredActions.Move_Horizontal);
        else
            _movementFactor = 0;

        AkSoundEngine.SetRTPCValue("Life", GetTotalVolumen());

        if (!isOnWall && !isClimbingNow && (!(isBackOnWall && _isFacingRight && (_movementFactor < 0 || _pushForce.x < 0)) && !(isBackOnWall && !_isFacingRight && (_movementFactor > 0 || _pushForce.x > 0))))
        {
            if (_pushForce.y == 0) {
                _rigidbody.velocity = new Vector2((_movementFactor * parameters.maxSpeed.x) + _pushForce.x, Mathf.Clamp(_rigidbody.velocity.y, -1 * parameters.maxSpeed.y, parameters.maxSpeed.y));
            }
            else
            {
                _rigidbody.velocity = new Vector2((_movementFactor * parameters.maxSpeed.x) + _pushForce.x, Mathf.Clamp(_pushForce.y, -1 * parameters.maxSpeed.y, parameters.maxSpeed.y));
            }
            transform.localScale -= new Vector3(Mathf.Abs(_movementFactor * parameters.downScaleValor), Mathf.Abs(_movementFactor * parameters.downScaleValor), 0) * Time.deltaTime;
        }
    }
    
    private void ActionJustPressed(InputActionEventData eventData)
    {
        if (!handleInput)
            return;

        if (CanJump)
        {
			jumpCount++;
            if(currentState == StateChanger.State.Drop)
            AudioMaster.PlayEvent(AudioMaster.SoundID.Drop_Jump);
            AudioMaster.PlayEvent(AudioMaster.SoundID.Voice_Jump);

            if (isOnClimbable)
            {
                _rigidbody.velocity = new Vector2(0, 0);
                isClimbingNow = true;
                if (_isFacingRight)
                    _rigidbody.AddForce(new Vector2(-100, parameters.JumpMagnitude));
                else
                    _rigidbody.AddForce(new Vector2(100, parameters.JumpMagnitude));

                walljumpCD = 0;

                return;
            } else
            {
                try
                {
                    int bla;
                    if (SteamUserStats.GetStat(DropSteamManager.STAT_JUMPS, out bla))
                    {
                        Debug.Log("Old jumps count = " + bla);
                        bla++;
                        SteamUserStats.SetStat(DropSteamManager.STAT_JUMPS, bla);
                        if (SteamUserStats.StoreStats())
                            Debug.Log("Store OK");
                        else
                            Debug.Log("Store failed");
                    }
                    else
                        Debug.Log("Failed getting jumps stats");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("Failed to access Steamworks data");
                }
            }
				
            _rigidbody.velocity = new Vector2(_rigidbody.velocity.x, 0);
            _rigidbody.AddForce(new Vector2(0, parameters.JumpMagnitude));
            _jumpIn = parameters.JumpFrequiency;
        }

		if(currentState == StateChanger.State.Cloud) 
		{
            if (avoidAction)
                return;

			_pushEvent.pushForce = new Vector2(0, parameters.pushForce);
			Push(_pushEvent);
			transform.localScale -= new Vector3(parameters.downScaleValor, parameters.downScaleValor, parameters.downScaleValor);
		}
    }

    private void Pause(InputActionEventData eventData)
    {
        if (!handleInput)
            return;

        GameManager.Instance.PauseGame(false);
    }

    public void IceDamage(float _damage, DamageSources source)
    {
        if (transform.localScale.x - parameters.minScale > _damage)
        {
            anim.SetTrigger("Crashing");
            AudioMaster.PlayEvent(AudioMaster.SoundID.Ice_Damage);
            AudioMaster.PlayEvent(AudioMaster.SoundID.Voice_Damage);
            transform.localScale -= new Vector3(_damage, _damage, 0);
        }
        else
        {
			Kill(AffectedStates.Drop | AffectedStates.Ice | AffectedStates.Cloud, source);
        }
    }

    private void Damage(StateSpecificFloat damageEvent)
	{
		if (((AffectedStates)currentState & damageEvent.damageEvent.affectedStates) == 0) return;

		switch (currentState)
        {
            case StateChanger.State.Drop:
                if ((transform.localScale.x - damageEvent.amount) < parameters.minScale)
                {
                    Kill(AffectedStates.Drop | AffectedStates.Cloud | AffectedStates.Ice, damageEvent.damageEvent.damageSource);
                }
                else
                {
                    transform.localScale -= new Vector3(damageEvent.amount, damageEvent.amount, 0);
                    time += Time.deltaTime;
                    if (time >= 0.15f)
                    {
                        AudioMaster.PlayEvent(AudioMaster.SoundID.Voice_Damage);
                        time = 0;
                    }

                }
                break;
            case StateChanger.State.Cloud:
                if ((transform.localScale.x - damageEvent.amount) < parameters.minScale)
                {
                    Kill(AffectedStates.Drop | AffectedStates.Cloud | AffectedStates.Ice, damageEvent.damageEvent.damageSource);
                }
                else
                {
                    AudioMaster.PlayEvent(AudioMaster.SoundID.Voice_Damage);
                    transform.localScale -= new Vector3(damageEvent.amount, damageEvent.amount, 0);
                }
                break;
            default:
                    IceDamage(damageEvent.amount, damageEvent.damageEvent.damageSource);
                break;
        }
	}

	private void Grow(StateSpecificFloat growthData)
	{
		if (((AffectedStates)currentState & growthData.damageEvent.affectedStates) == 0) return;

		if (transform.localScale.x < parameters.maxScale)
		{
			switch (currentState)
			{
				case StateChanger.State.Drop:
                    if(growthData.fpsDependant)
                        transform.localScale += new Vector3(growthData.amount, growthData.amount, 0) * Time.deltaTime;
                    else
                        transform.localScale += new Vector3(growthData.amount, growthData.amount, 0);
                    break;
				case StateChanger.State.Cloud:
                    if (growthData.fpsDependant)
                        transform.localScale += new Vector3(growthData.amount, growthData.amount, 0) * Time.deltaTime;
                    else
                        transform.localScale += new Vector3(growthData.amount, growthData.amount, 0);
                    break;
				default:
                    if (growthData.fpsDependant)
                        transform.localScale += new Vector3(growthData.amount, growthData.amount, 0) * Time.deltaTime;
                    else
                        transform.localScale += new Vector3(growthData.amount, growthData.amount, 0);
                    break;
			}

            if (!growthData.fpsDependant && soundFxAllowed)
            {
                AudioMaster.PlayEvent(AudioMaster.SoundID.Healing_Water);
                soundFxAllowed = false;
                StartCoroutine(AllowSoundFxAfterDelay(1));
            }
        }

	}

    public void Kill(DamageEvent damageEvent)
    {
        if (IsDead || GameManager.Instance.CurrentGameState != GameManager.GameState.Playing)
            return;

		if (((AffectedStates)currentState & damageEvent.affectedStates) == 0) return;

		try
		{
			int bla;
			if (SteamUserStats.GetStat(DropSteamManager.STAT_DEATHS_MILK, out bla))
			{
				Debug.Log("Old milk death count = " + bla);
				bla++;
				SteamUserStats.SetStat(DropSteamManager.STAT_DEATHS_MILK, bla);
				if(SteamUserStats.StoreStats())
					Debug.Log("Store OK");
				else
					Debug.Log("Store failed");
			}
			else
				Debug.Log("Failed getting milk death stats");
		}
		catch (System.Exception e)
		{
			Debug.LogWarning("Failed to access Steamworks data");
		}

        switch (currentState)
        {
            case StateChanger.State.Drop:
				//deathClip = _sounds.dropDeath;
                //SoundManager.Instance.PlaySound2D(_sounds.voiceDeath[UnityEngine.Random.Range(0, _sounds.voiceDeath.Length)], transform.position);
                AudioMaster.PlayEvent(AudioMaster.SoundID.Drop_Death);
                break;
            case StateChanger.State.Cloud:
				//deathClip = _sounds.cloudDeath;
				//SoundManager.Instance.PlaySound2D(_sounds.voiceDeath[UnityEngine.Random.Range(0, _sounds.voiceDeath.Length)], transform.position);
                AudioMaster.PlayEvent(AudioMaster.SoundID.Wind_Death);
                _rigidbody.isKinematic = true;
                break;
			default:
				//deathClip = _sounds.iceDeath;
                AudioMaster.PlayEvent(AudioMaster.SoundID.Ice_Death);
                break;
        }
		//SoundManager.Instance.PlaySound2D(deathClip, transform.position);

		SaveAnalytics(AnalyticState.Died);

        handleInput = false;
        IsDead = true;
        anim.SetBool("death", IsDead);
        GameManager.Instance.CurrentGameState = GameManager.GameState.GameOver;
        if (TimeAtackManager.Instance.currentGameMode == GameMode.Normal)
            GUIManager.Instance.SetGameOverActive(true);
        else
        {
            StartCoroutine(RestartWithDelay(1.5f));
        }
    }

    public void Kill(AffectedStates _affectedStates, DamageSources _source)
    {
        Kill(new DamageEvent() { affectedStates = _affectedStates, damageSource = _source });
    }

    public void Push(object pushEvent)
    {
        if (IsDead) return;

        if (pushEvent is StateSpecificVector2)
            Push((StateSpecificVector2)pushEvent);
    }

    private void Push(StateSpecificVector2 pushEvent)
    {
        if (((AffectedStates)currentState & pushEvent.affectedStates) == 0) return;

        switch (currentState)
        {
            case StateChanger.State.Drop:
                _pushForce.x = pushEvent.pushForce.x;
                _pushForce.y = pushEvent.pushForce.y;
                break;
            case StateChanger.State.Cloud:
                _pushForce.x = pushEvent.pushForce.x;
                _pushForce.y = pushEvent.pushForce.y;
                break;
            default:
                _pushForce.x = pushEvent.pushForce.x;
                _pushForce.y = pushEvent.pushForce.y;
                break;
        }
    }

    private void Warning(bool emit)
    {
        if (currentState == StateChanger.State.Drop && !IsDead)
        {
            emmiter.enabled = emit;
        }
        else
        {
            emmiter.enabled = false;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (GameManager.Instance.CurrentGameState != GameManager.GameState.Playing && GameManager.Instance.CurrentGameState != GameManager.GameState.LevelEnded)
            return;

        ParametersChanger parametersChanger = other.GetComponent<ParametersChanger>();
		if (parametersChanger != null)
        {
			_overrideParameters = parametersChanger.newParameters;
        }
        if (other.GetComponent<GameManager>() != null)
		{
            _rigidbody.isKinematic = true;
            AudioMaster.PlayEvent(AudioMaster.SoundID.Win);
            SaveAnalytics(AnalyticState.Won);
		}
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponent<ParametersChanger>() != null)
        {
            _overrideParameters = null;
        }
    }

    void OnCollisionEnter2D(Collision2D other)
    {
        if (GameManager.Instance.CurrentGameState != GameManager.GameState.Playing)
            return;

        _pushForce = Vector2.zero;
    }

    void OnDestroy()
    {
        if (!isShuttingDown)
        {
            InputProcesor.Instance.MyPlayer.RemoveInputEventDelegate(ActionJustPressed);
            InputProcesor.Instance.MyPlayer.RemoveInputEventDelegate(Pause);
        }
    }

    void OnApplicationQuit()
    {
        isShuttingDown = true;
    }

	public void SaveAnalytics(AnalyticState state)
	{
//		StreamWriter stream = new StreamWriter("Analytics.txt", true);
//		stream.WriteLine("---------------------");
//		stream.WriteLine("Time      = " + DateTime.Now.Hour.ToString("00") + ":" + DateTime.Now.Minute.ToString("00") + "." + DateTime.Now.Second.ToString("00"));
//		stream.WriteLine("Event     = " + state.ToString());
//		stream.WriteLine("Level     = " + SceneManager.GetActiveScene().name);
//		stream.WriteLine("Volume    = " + (scaleRatio * 100).ToString("0.00") + "%");
//		stream.WriteLine("Position  = " + (Vector2)transform.position);
//		stream.WriteLine("Jump count= " + jumpCount);
//		stream.Close();
	}

    public void Reset()
    {
        IsDead = false;
        anim.Rebind();
        _collider.radius = 0.25f;
        _collider.offset = Vector2.zero;
        ProCamera2D.Instance.RemoveAllCameraTargets();
        ProCamera2D.Instance.AddCameraTarget(this.transform);
        _volIni = transform.localScale.x;
        _isFacingRight = !(transform.localRotation.y > 0);
        gameObject.SetActive(true);
        if(currentState == StateChanger.State.Drop)
        {
            _stateChanger.canChange = true;
            parameters.jumpRestrictions = ControllerParameters.JumpBehavior.CanJumpOnGroundAndWall;
            parameters.maxSpeed.x = _initialDropSpeed;
        }
        _stateChanger.ChangeState(_initialState);
        if (warningParticles != null)
        {
            warningParticles.Play();
            emmiter.enabled = false;
        }
        soundFxAllowed = true;
    }

    IEnumerator RestartWithDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        PlayerSpawner.Instance.Spawn();
        GameManager.Instance.Reset();
        GameManager.Instance.CurrentGameState = GameManager.GameState.Playing;
    }

    IEnumerator AllowSoundFxAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        soundFxAllowed = true;
    }

}