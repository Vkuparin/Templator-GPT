using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Templator.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(name);
    }
    public void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class Template : Observable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set => Set(ref field, value); } = "Untitled template";
    public string Language { get; set => Set(ref field, value); } = "en";
    public string To { get; set => Set(ref field, value); } = "{{recipient}}";
    public string Cc { get; set => Set(ref field, value); } = "{{cc_list}}";
    public string Subject { get; set => Set(ref field, value); } = "";
    public string Body { get; set => Set(ref field, value); } = "";
    public ObservableCollection<Variable> Variables { get; set; } = [];
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
}

public sealed class Variable : Observable
{
    public string Key { get; set; } = "";
    public string Label { get; set => Set(ref field, value); } = "";
    public string Example { get; set => Set(ref field, value); } = "";
    public bool Required { get; set => Set(ref field, value); } = true;
    public bool Customized { get; set; }
    [JsonIgnore] public bool Referenced { get; set => Set(ref field, value); }
    [JsonIgnore] public string Value { get; set => Set(ref field, value); } = "";
}

public sealed class Settings
{
    public string DefaultTo { get; set; } = "";
    public string DefaultCc { get; set; } = "";
    public int MailtoLengthThreshold { get; set; } = 1800;
}

public sealed class StoreData
{
    public int Version { get; set; } = 1;
    public List<Template> Templates { get; set; } = [];
    public Settings Settings { get; set; } = new();
}

public static class Samples
{
    public static Template Sap(bool finnish = false)
    {
        var template = new Template
        {
            Name = finnish ? "SAP · Projektin avaus" : "SAP · Project creation",
            Language = finnish ? "fi" : "en",
            Subject = finnish ? "Projektin avaus: {{order_number}} — {{customer_name}}" : "New project: {{order_number}} — {{customer_name}}",
            Body = finnish
                ? "Hei,\n\nVoisitteko avata SAP-projektin seuraavalle tilaukselle?\n\nAsiakas: {{customer_name}}\nTilausnumero: {{order_number}}\nToimituspäivä: {{delivery_date}}\n\nKiitos ja ystävällisin terveisin,\n{{sender_name}}"
                : "Hello,\n\nPlease create a new project in SAP for the following order.\n\nCustomer: {{customer_name}}\nOrder number: {{order_number}}\nDelivery date: {{delivery_date}}\n\nThank you,\n{{sender_name}}"
        };
        VariableSyntax.Synchronize(template);
        foreach (var variable in template.Variables)
        {
            variable.Required = variable.Key is not ("cc_list" or "delivery_date");
            variable.Example = variable.Key switch
            {
                "recipient" => "sap-team@example.com",
                "cc_list" => "Optional · separate addresses with commas",
                "customer_name" => "Acme Oy",
                "order_number" => "45001234",
                "delivery_date" => "2026-11-30",
                "sender_name" => "Your name",
                _ => ""
            };
            variable.Customized = true;
        }
        return template;
    }
}
