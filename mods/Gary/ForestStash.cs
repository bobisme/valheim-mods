namespace Gary
{
    // Persist only real gathered items, bounded to six total and two feathers.
    internal readonly struct ForestStash
    {
        internal const int Capacity=6,FeatherCapacity=2;
        internal readonly int Berries,Blueberries,Mushrooms,Feathers;
        internal ForestStash(int berries,int blueberries,int mushrooms,int feathers=0)
        {
            Berries=Clamp(berries,Capacity);
            Blueberries=Clamp(blueberries,Capacity-Berries);
            Mushrooms=Clamp(mushrooms,Capacity-Berries-Blueberries);
            Feathers=Clamp(feathers,System.Math.Min(FeatherCapacity,Capacity-Berries-Blueberries-Mushrooms));
        }
        private static int Clamp(int value,int maximum) => System.Math.Max(0,System.Math.Min(maximum,value));
        internal int Count => Berries+Blueberries+Mushrooms+Feathers;
        internal int At(int kind) => kind==0?Berries:kind==1?Blueberries:kind==2?Mushrooms:kind==3?Feathers:0;
        internal bool TryAdd(int kind,int amount,out ForestStash next)
        {
            next=this;if(kind<0||kind>3||amount<=0||amount>Capacity-Count||kind==3&&amount>FeatherCapacity-Feathers)return false;
            next=new ForestStash(Berries+(kind==0?amount:0),Blueberries+(kind==1?amount:0),Mushrooms+(kind==2?amount:0),Feathers+(kind==3?amount:0));return true;
        }
        internal bool TryTake(int kind,out ForestStash next)
        {
            next=this;if(kind<0||kind>3||At(kind)<=0)return false;
            next=new ForestStash(Berries-(kind==0?1:0),Blueberries-(kind==1?1:0),Mushrooms-(kind==2?1:0),Feathers-(kind==3?1:0));return true;
        }
        internal int GiftKind(int roll)
        {
            if(roll<0||roll>=100||Count==0)return -1;
            if(Feathers>0&&(Count==Feathers||roll<20))return 3;
            int first=roll%3;
            for(int i=0;i<3;i++)if(At((first+i)%3)>0)return (first+i)%3;
            return -1;
        }
    }
}
