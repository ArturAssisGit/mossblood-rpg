using UnityEngine;

public partial class DecayAtmosphere
{
    const int Swarms=4,FliesPerHorse=28;
    readonly HorseMount[] swarmHorses=new HorseMount[Swarms],selectedHorses=new HorseMount[Swarms];
    readonly float[] selectedDistances=new float[Swarms];
    readonly Fly[] horseFlies=new Fly[Swarms*FliesPerHorse];
    readonly ParticleSystem.Particle[] flyBuffer=new ParticleSystem.Particle[Swarms*FliesPerHorse];
    struct Fly{public Vector3 local,goal,velocity;public float next,size,seed;}
    void ClearHorseSwarms()
    {
        System.Array.Clear(swarmHorses,0,Swarms);System.Array.Clear(selectedHorses,0,Swarms);
        System.Array.Clear(horseFlies,0,horseFlies.Length);
    }
    void SelectHorseSwarms()
    {
        for(int i=0;i<Swarms;i++){selectedHorses[i]=null;selectedDistances[i]=float.MaxValue;}
        foreach(var horse in horses)
        {
            if(horse==null || !horse.IsPresent)continue;
            float distance=(horse.transform.position-player.position).sqrMagnitude;
            if(distance>32f*32f)continue;
            if(horse==HorseMount.Ridden)distance=-1;
            for(int i=0;i<Swarms;i++)if(distance<selectedDistances[i])
            {
                for(int j=Swarms-1;j>i;j--){selectedDistances[j]=selectedDistances[j-1];selectedHorses[j]=selectedHorses[j-1];}
                selectedDistances[i]=distance;selectedHorses[i]=horse;break;
            }
        }
        for(int i=0;i<Swarms;i++)
        {
            bool keep=false;for(int j=0;j<Swarms;j++)if(swarmHorses[i]!=null && swarmHorses[i]==selectedHorses[j])keep=true;
            if(!keep)swarmHorses[i]=null;
        }
        foreach(var selected in selectedHorses)
        {
            if(selected==null)continue;bool present=false;
            foreach(var active in swarmHorses)if(active==selected)present=true;
            if(present)continue;
            for(int slot=0;slot<Swarms;slot++)if(swarmHorses[slot]==null)
            {
                swarmHorses[slot]=selected;
                for(int j=0;j<FliesPerHorse;j++)
                {
                    Vector3 local=Random.insideUnitSphere;
                    horseFlies[slot*FliesPerHorse+j]=new Fly{local=local,goal=local,next=Time.time+Random.Range(.1f,.6f),
                        size=Random.Range(.032f,.052f),seed=Random.Range(0,100f)};
                }
                break;
            }
        }
    }
    void TickHorseFlies(float dt)
    {
        int written=0;
        for(int slot=0;slot<Swarms;slot++)
        {
            var horse=swarmHorses[slot];
            if(horse==null || !horse.IsPresent || (horse.transform.position-player.position).sqrMagnitude>36f*36f)continue;
            Bounds bounds=horse.AtmosphereBounds;
            Vector3 scale=new Vector3(Mathf.Clamp(bounds.extents.x,.55f,1.1f),Mathf.Clamp(bounds.extents.y*.6f,.35f,.7f),Mathf.Clamp(bounds.extents.z,.7f,1.4f));
            float activity=DecayAtmosphereRules.FlyActivity(rain,horse.CurrentSpeed)*Mathf.Clamp(horseFlyAmount,0,2);
            int count=Mathf.Clamp(Mathf.RoundToInt(FliesPerHorse*activity),0,FliesPerHorse);
            for(int j=0;j<count;j++)
            {
                int index=slot*FliesPerHorse+j;Fly fly=horseFlies[index];
                if(Time.time>=fly.next)
                {
                    fly.goal=Random.insideUnitSphere;
                    if(Random.value<.14f)fly.goal=new Vector3(Random.Range(-.4f,.4f),.4f,Random.Range(-.5f,.6f));
                    fly.next=Time.time+Random.Range(.12f,.55f);
                }
                Vector3 delta=fly.goal-fly.local;
                fly.velocity=Vector3.Lerp(fly.velocity,Vector3.ClampMagnitude(delta*7,2.6f),1-Mathf.Exp(-12*dt));
                fly.local=Vector3.ClampMagnitude(fly.local+fly.velocity*dt,1.15f);
                Vector3 buzz=new Vector3(Mathf.Sin(Time.time*39+fly.seed),Mathf.Sin(Time.time*31+fly.seed*2),Mathf.Cos(Time.time*35+fly.seed))*.012f;
                flyBuffer[written++]=new ParticleSystem.Particle{position=bounds.center+Vector3.up*.15f+Vector3.Scale(fly.local,scale)+buzz,
                    startSize=fly.size,startColor=new Color(.12f,.11f,.09f,1),startLifetime=10,remainingLifetime=10};
                horseFlies[index]=fly;
            }
        }
        pools[3].SetParticles(flyBuffer,written);
    }
}
