using CRM2015_Plugin.AutoNumber;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.ServiceModel;
using System.Text;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;

namespace CRM2015_Plugin_AutoNumber
{
    public class CounterPlugin : IPlugin
    {
        internal IOrganizationService _orgService
        {
            get;
            private set;
        }

        internal IPluginExecutionContext _context
        {
            get;
            private set;
        }

        internal au_counterconfiguration _counterEntity
        {
            get;
            private set;
        }

        internal Entity _callingEntity
        {
            get;
            private set;
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            try
            {
                int? Nextnumber;
                int? Incrementby;

                if (serviceProvider == null)
                {
                    throw new ArgumentNullException("serviceProvider");
                }
                _context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
                ITracingService _trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
                IOrganizationServiceFactory service = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                _orgService = service.CreateOrganizationService(new Guid?(_context.UserId));
                string messageName = _context.MessageName;

                if (messageName == null)
                {
                    throw new ArgumentNullException("messageName");
                }

                //set callingEntity and CounterConfigration entity
                if (_context.PrimaryEntityName != "au_counterconfiguration")
                {
                    _counterEntity = GetCounterEntity().ToEntity<au_counterconfiguration>();
                    string entityid = string.Empty;

                    //if lock key is equal then current key in process, then is record ready for writing 
                    string currentid = _context.PrimaryEntityId.ToString();
                    while (entityid != currentid)
                    {
                        //peridical checking, if record is free for processing 
                        System.Threading.Thread.Sleep(25);
                        _counterEntity = GetCounterEntity().ToEntity<au_counterconfiguration>();
                        entityid = _counterEntity.au_entityid;
                        //if entityid is empty, then record is free for processing. 
                        if (string.IsNullOrEmpty(entityid))
                        {
                            Entity updatedCounter = new Entity() { LogicalName = _counterEntity.LogicalName, Id = _counterEntity.Id, };
                            //only key value (entityid) will be saved, this key locking current record 
                            updatedCounter["au_entityid"] = currentid;
                            //create UpdateRequest, Update(_counterEntity) not working 
                            UpdateRequest upd = new UpdateRequest() { Target = updatedCounter, ConcurrencyBehavior = ConcurrencyBehavior.AlwaysOverwrite };
                            _orgService.Execute(upd);
                        }
                    }

                    if (messageName == "Create")
                    {
                        int currentYear = DateTime.Now.Year;
                        StringBuilder stringBuilder = new StringBuilder();

                        //add prefix                    
                        string Prefix = _counterEntity.au_Prefix?.Replace("[YYYY]", currentYear.ToString());
                        Prefix = Prefix?.Replace("[YY]", currentYear.ToString().Substring(2));
                        stringBuilder.Append(Prefix);

                        //check increment by                    
                        Incrementby = _counterEntity.au_IncrementBy;

                        //set next number with or without zeropads
                        if (_counterEntity.au_resetnumber == null || _counterEntity.au_lastactivity == null || !_counterEntity.au_resetnumber.Value || _counterEntity.au_lastactivity.Value.Year == currentYear)
                            Nextnumber = _counterEntity.au_NextNumber;
                        else
                            Nextnumber = Incrementby;

                        //set next number with or without zeropads                    
                        if (!_counterEntity.au_ZeroPad.Value)
                        {
                            stringBuilder.Append(Nextnumber.ToString());
                        }
                        else
                        {
                            string str1 = Nextnumber.ToString();
                            stringBuilder.Append(str1.PadLeft(_counterEntity.au_Length.Value, '0'));
                        }

                        //add suffix                   
                        string Suffix = _counterEntity.au_Suffix?.Replace("[YYYY]", currentYear.ToString());
                        Suffix = Suffix?.Replace("[YY]", currentYear.ToString().Substring(2));
                        stringBuilder.Append(Suffix);

                        //check increment by                    
                        Incrementby = _counterEntity.au_IncrementBy;

                        //set next number
                        _counterEntity.au_NextNumber = new int?(Nextnumber.GetValueOrDefault() + Incrementby.GetValueOrDefault());

                        //clean entity id (process is finished, the current record will be free)
                        _counterEntity.au_entityid = null;

                        //last activity with getting number
                        _counterEntity.au_lastactivity = DateTime.Now;

                        //update counter entity
                        _trace.Trace("Action: Update counter record");
                        _orgService.Update(_counterEntity);

                        //set counter to field in callingEntity
                        _trace.Trace("Action: set field name with value " + stringBuilder.ToString());
                        _callingEntity.Attributes[_counterEntity.au_FieldName] = stringBuilder.ToString();
                    }
                }
                else if (messageName == "Create")
                {
                    _counterEntity = ((Entity)_context.InputParameters["Target"]).ToEntity<au_counterconfiguration>();
                    createSDKMessage();
                }
                else if (messageName == "Delete")
                {
                    Guid id = ((EntityReference)_context.InputParameters["Target"]).Id;
                    au_counterconfiguration entity2 = _orgService.Retrieve("au_counterconfiguration", ((EntityReference)_context.InputParameters["Target"]).Id, new ColumnSet(true)).ToEntity<au_counterconfiguration>();
                    _orgService.Delete("sdkmessageprocessingstep", new Guid(entity2.au_PluginStepID));
                }
                else if (messageName == "SetState" || messageName == "SetStateDynamicEntity")
                {
                    if (_context.InputParameters.Contains("EntityMoniker") && _context.InputParameters["EntityMoniker"] is EntityReference)
                    {
                        var myEntity = (EntityReference)_context.InputParameters["EntityMoniker"];
                        OptionSetValue state = (OptionSetValue)_context.InputParameters["State"];
                        OptionSetValue status = (OptionSetValue)_context.InputParameters["Status"];
                        au_counterconfiguration entity2 = _orgService.Retrieve("au_counterconfiguration", myEntity.Id, new ColumnSet(true)).ToEntity<au_counterconfiguration>();
                        ToggleProcessState(state, status, new Guid(entity2.au_PluginStepID));
                    }

                }
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException("An error occured in Autonumber plugin", ex);
            }
        }

        private Entity GetCounterEntity()
        {
            _callingEntity = (Entity)_context.InputParameters["Target"];
            QueryExpression queryExpression = new QueryExpression("au_counterconfiguration")
            {
                ColumnSet = new ColumnSet(new string[] { "au_incrementby", "au_zeropad", "au_suffix", "au_prefix", "au_nextnumber", "au_length", "au_fieldname", "au_entityid", "au_lastactivity", "au_resetnumber" }),
                NoLock = false,
                Criteria = new FilterExpression()
            };
            queryExpression.Criteria.AddCondition("au_entityname", ConditionOperator.Equal, new object[] { _context.PrimaryEntityName });
            queryExpression.Criteria.AddCondition("statecode", ConditionOperator.Equal, new object[] { 0 });
            EntityCollection entityCollection = _orgService.RetrieveMultiple(queryExpression);
            if (entityCollection.Entities.Count == 0)
            {
                throw new InvalidPluginExecutionException("Error: An Counter is configured for this entity although cannot locate the ID Generator config record.");
            }
            return entityCollection.Entities[0];
        }

        private void createSDKMessage()
        {
            SdkMessageProcessingStep sdkMessageProcessingStep = new SdkMessageProcessingStep()
            {
                Mode = new OptionSetValue(0),
                Name = string.Format("Counter for the {0} Entity", _counterEntity.au_entityname),
                Rank = new int?(1)
            };

            //check if entity exist
            if (!DoesEntityExist(_counterEntity.au_entityname))
                throw new InvalidPluginExecutionException(string.Format("Entity {0} does not exist!", _counterEntity.au_entityname));

            //check if field exist
            if (!DoesFieldExist(_counterEntity.au_entityname, _counterEntity.au_FieldName))
                throw new InvalidPluginExecutionException(string.Format("Field {0} does not exist in Entity {1}!", _counterEntity.au_FieldName, _counterEntity.au_entityname));

            Guid _createMessage = SetCreateMessageId();
            sdkMessageProcessingStep.SdkMessageId = new EntityReference("sdkmessage", _createMessage);
            Guid _sdkFilterId = SetSdkMessageFilterId(_counterEntity.au_entityname, _createMessage);
            sdkMessageProcessingStep.SdkMessageFilterId = new EntityReference("sdkmessagefilter", _sdkFilterId);
            Guid _pluginType = SetPluginTypeId();
            sdkMessageProcessingStep.EventHandler = new EntityReference("plugintype", _pluginType);
            sdkMessageProcessingStep.Stage = new OptionSetValue(20);
            sdkMessageProcessingStep.SupportedDeployment = new OptionSetValue(0);
            Guid guid = _orgService.Create(sdkMessageProcessingStep);
            _counterEntity.au_PluginStepID = guid.ToString();

        }

        private bool DoesEntityExist(String entityName)
        {
            var entityRequest = new RetrieveAllEntitiesRequest
            {
                RetrieveAsIfPublished = true,
                EntityFilters = EntityFilters.Entity
            };

            var entityResponse = (RetrieveAllEntitiesResponse)_orgService.Execute(entityRequest);
            foreach (EntityMetadata _entityMetadata in entityResponse.EntityMetadata)
            {
                if (_entityMetadata.LogicalName == _counterEntity.au_entityname)
                {
                    return true;
                }

            }
            return false;
        }

        private bool DoesFieldExist(String entityName, String fieldName)
        {
            RetrieveEntityRequest request = new RetrieveEntityRequest
            {
                EntityFilters = EntityFilters.Attributes,
                LogicalName = entityName
            };
            RetrieveEntityResponse response = (RetrieveEntityResponse)_orgService.Execute(request);
            return response.EntityMetadata.Attributes.FirstOrDefault(element => element.LogicalName == fieldName) != null;
        }

        private Guid SetCreateMessageId()
        {
            try
            {
                QueryByAttribute queryByAttribute = new QueryByAttribute()
                {
                    EntityName = "sdkmessage",
                    ColumnSet = new ColumnSet()
                    {
                        AllColumns = true
                    }
                };
                QueryByAttribute queryByAttribute1 = queryByAttribute;
                queryByAttribute1.Attributes.Add("name");
                queryByAttribute1.Values.Add("Create");
                EntityCollection entityCollection = _orgService.RetrieveMultiple(queryByAttribute1);
                if (entityCollection.Entities.Count == 0)
                {
                    throw new InvalidPluginExecutionException("Error: The Create Message collection was empty");
                }
                Guid _createMessage = ((SdkMessage)entityCollection[0]).Id;
                return _createMessage;
            }
            catch (FaultException<OrganizationServiceFault> faultException1)
            {
                FaultException<OrganizationServiceFault> faultException = faultException1;
                throw new InvalidPluginExecutionException(string.Concat(faultException.Message, Environment.NewLine, faultException.StackTrace));
            }
            catch (Exception exception1)
            {
                Exception exception = exception1;
                throw new InvalidPluginExecutionException(string.Concat(exception.Message, Environment.NewLine, exception.StackTrace));
            }
        }

        private Guid SetPluginTypeId()
        {
            try
            {
                QueryByAttribute queryByAttribute = new QueryByAttribute()
                {
                    EntityName = "plugintype",
                    ColumnSet = new ColumnSet()
                    {
                        AllColumns = true
                    }
                };
                QueryByAttribute queryByAttribute1 = queryByAttribute;
                queryByAttribute1.Attributes.Add("assemblyname");
                queryByAttribute1.Values.Add("CRM2015_Plugin.AutoNumber");
                EntityCollection entityCollection = _orgService.RetrieveMultiple(queryByAttribute1);
                if (entityCollection.Entities.Count == 0)
                {
                    throw new InvalidPluginExecutionException("Error: The Plugin Type collection was empty");
                }
                Guid _pluginType = ((PluginType)entityCollection[0]).Id;
                return _pluginType;
            }
            catch (FaultException<OrganizationServiceFault> faultException1)
            {
                FaultException<OrganizationServiceFault> faultException = faultException1;
                throw new InvalidPluginExecutionException(string.Concat(faultException.Message, Environment.NewLine, faultException.StackTrace));
            }
            catch (Exception exception1)
            {
                Exception exception = exception1;
                throw new InvalidPluginExecutionException(string.Concat(exception.Message, Environment.NewLine, exception.StackTrace));
            }
        }

        private Guid SetSdkMessageFilterId(string entityName, Guid _createMessage)
        {
            try
            {
                QueryByAttribute queryByAttribute = new QueryByAttribute()
                {
                    EntityName = "sdkmessagefilter",
                    ColumnSet = new ColumnSet()
                    {
                        AllColumns = true
                    }
                };
                QueryByAttribute queryByAttribute1 = queryByAttribute;
                queryByAttribute1.Attributes.Add("primaryobjecttypecode");
                queryByAttribute1.Values.Add(entityName);
                queryByAttribute1.Attributes.Add("sdkmessageid");
                queryByAttribute1.Values.Add(_createMessage);
                EntityCollection entityCollection = _orgService.RetrieveMultiple(queryByAttribute1);
                if (entityCollection.Entities.Count == 0)
                {
                    throw new InvalidPluginExecutionException("Error: The SDK Message Filter collection was empty");
                }
                Guid _sdkFilterId = ((SdkMessageFilter)entityCollection[0]).Id;
                return _sdkFilterId;
            }
            catch (FaultException<OrganizationServiceFault> faultException1)
            {
                FaultException<OrganizationServiceFault> faultException = faultException1;
                throw new InvalidPluginExecutionException(string.Concat(faultException.Message, Environment.NewLine, faultException.StackTrace));
            }
            catch (Exception exception1)
            {
                Exception exception = exception1;
                throw new InvalidPluginExecutionException(string.Concat(exception.Message, Environment.NewLine, exception.StackTrace));
            }
        }

        private void ToggleProcessState(OptionSetValue pluginStateCode, OptionSetValue pluginStatusCode, Guid id)
        {
            _orgService.Execute(new SetStateRequest
            {
                EntityMoniker = new EntityReference("sdkmessageprocessingstep", id),
                State = pluginStateCode,
                Status = pluginStatusCode
            });
        }
    }
}