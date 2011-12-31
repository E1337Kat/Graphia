using UnityEngine;
using System.Collections;

public class WalkController : MonoBehaviour
{
	/// <summary>
	/// A reference to the controller to speed runtime.
	/// </summary>
	private CharacterController controller;
	/// <summary>
	/// The speed at which to move.
	/// </summary>
	public float speed;
	/// <summary>
	/// The destination to move toward.
	/// </summary>
	public Vector3 destination;
	
	/// <summary>
	/// Initialize the destination to the object's position and the previous position to the current position.
	/// </summary>
	void Start ()
	{
		controller = GetComponent<CharacterController>();
		destination = transform.position;
	}
	
	/// <summary>
	/// If the object is not at it's destination, move toward it. If it can no longer move, reset it's destination. If we're not grounded, fall.
	/// </summary>
	void Update ()
	{
		Vector3 direction;
		
		// If we are not yet at our destination:
		if(destination!=transform.position)
		{
			
			destination.y = transform.position.y;
			
			transform.LookAt(destination);
			
			direction = destination - transform.position;
			
			controller.SimpleMove(speed * direction.normalized);
		}
		
		// If we haven't moved (and are therefore stuck):
		if(controller.velocity.sqrMagnitude==0)
		{
			// Reset the destination, and
			destination = transform.position;
			// Switch to the idle animation.
			animation.Play("idle");
		}
		else
		{
			// Otherwise, use the walk animation.
			animation.Play("walk");
		}
	}
}
