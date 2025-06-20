﻿namespace Soqet3.Structs;

public struct Metadata
{
    public string Channel { get; set; }
    public string Address { get; internal set; }
    public DateTime DateTime { get; set; }
    public string Sender { get; set; }
    public bool Guest { get; set; }
}
