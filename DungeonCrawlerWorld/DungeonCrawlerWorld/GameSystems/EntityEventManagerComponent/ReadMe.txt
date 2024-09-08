EntityManagerComponent
	Responsible to maintaining the Entity + EntityComponent registry.
	Responsible for maintaining, iterating through, and distributing the Action registry queue to the various Entities in the Entity register
	Should NOT create or update entities directly. 
	Should NOT define components or their behavior.
	Should NOT define the components an action needs
	SHOULD contain the logic to determine if an entity contains the components an action needs and apply that action to the entity