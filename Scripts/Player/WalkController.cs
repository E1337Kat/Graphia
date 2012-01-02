using UnityEngine;
using System.Collections;

/// <summary>
/// This is the WalkController component. It is used to move the player and interface with the KeyController and
/// ClickController. It has a public destination vector that it moves the attached transform toward at speed defined in the
/// public float speed. This class current moves the player by pointing it at the destination then moving straight toward it
/// at a rate of speed. In a refined version, this class could use pathfinding extension and A* algorithms to move more 
/// intelligently. As well, the player's rotation could be interpolated allowing for more smooth animation. In this version,
/// this class handles animation by diastatically switching between idle and walk animations according to whether or not the
/// player is moving. In a refined version, this class change the speed of the animation and blend it with others to more
/// closely approximate the animation to the objects movement.
/// </summary>
public class WalkController : MonoBehaviour
{
	/// <summary>
	/// This is a reference to the character controller component used to speed method calls on this class at runtime by
	/// preventing the use of the GetComponent method at any point other than initialization. It is assigned in the Start
	/// method.
	/// </summary>
	private CharacterController controller;
	/// <summary>
	/// This float represents the speed at which the player should move.
	/// </summary>
	public float speed;
	/// <summary>
	/// This is a Vector3 representing the destination for the player to move toward.
	/// </summary>
	public Vector3 destination;
	
	/// <summary>
	/// This is the Start method, called at the initialization of the class and used to set Character Controller to the
	/// instance of Character Controller on this object.
	/// </summary>
	void Start ()
	{
		// Initialize necessary variables.
		controller = GetComponent<CharacterController>();
		destination = transform.position;
		
		// Set all animations to loop.
		animation.wrapMode = WrapMode.Loop;
	}
	
	/// <summary>
	/// This is the Update method. It is called every frame to adjust the player's rotation and position toward the destination
	/// As well, the animation is changed according to the speed. This method needs to be greatly improved.
	/// </summary>
	void Update ()
	{
		Vector3 direction;
		
		// If we are not yet at our destination:
		if(destination!=transform.position)
		{
			// Eleminate the y difference.
			destination.y = transform.position.y;
			// Point toward the destination
			transform.LookAt(destination);
			// Find the difference in the current position and the destination
			direction = destination - transform.position;
			// Move toward it, using standard Unity calls to properly handle collisions.
			controller.SimpleMove(speed * direction.normalized);
		}
		
		// If we have arrived at our destination.
		if(Vector3.Distance(destination, transform.position)<Time.deltaTime)
		{
			// Reset the destination, and
			destination = transform.position;
			// Switch to the idle animation.
			animation.CrossFade("idle");
		}
		else
		{
			// Otherwise, use the walk animation.
			animation.CrossFade("walk");
		}
	}
}
