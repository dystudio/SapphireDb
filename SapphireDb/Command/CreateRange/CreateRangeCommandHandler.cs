﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SapphireDb.Attributes;
using SapphireDb.Helper;
using SapphireDb.Internal;
using SapphireDb.Models;
using SapphireDb.Models.Exceptions;

namespace SapphireDb.Command.CreateRange
{
    class CreateRangeCommandHandler : CommandHandlerBase, ICommandHandler<CreateRangeCommand>
    {
        private readonly IServiceProvider serviceProvider;

        public CreateRangeCommandHandler(DbContextAccesor contextAccessor, IServiceProvider serviceProvider)
            : base(contextAccessor)
        {
            this.serviceProvider = serviceProvider;
        }

        public Task<ResponseBase> Handle(HttpInformation context, CreateRangeCommand command)
        {
            SapphireDbContext db = GetContext(command.ContextName);
            KeyValuePair<Type, string> property = db.GetType().GetDbSetType(command.CollectionName);

            if (property.Key != null)
            {
                try
                {
                    return Task.FromResult(CreateObjects(command, property, context, db));
                }
                catch (Exception ex)
                {
                    return Task.FromResult(command.CreateExceptionResponse<CreateRangeResponse>(ex));
                }
            }

            return Task.FromResult(
                command.CreateExceptionResponse<CreateRangeResponse>(new CollectionNotFoundException()));
        }

        private ResponseBase CreateObjects(CreateRangeCommand command, KeyValuePair<Type, string> property,
            HttpInformation context, SapphireDbContext db)
        {
            object[] newValues = command.Values.Values<JObject>().Select(newValue => newValue.ToObject(property.Key))
                .ToArray();

            CreateRangeResponse response = new CreateRangeResponse
            {
                ReferenceId = command.ReferenceId,
                Results = newValues.Select(value =>
                {
                    if (!property.Key.CanCreate(context, value, serviceProvider))
                    {
                        return (CreateResponse) command.CreateExceptionResponse<CreateResponse>(
                            new UnauthorizedException("The user is not authorized for this action"));
                    }

                    return SetPropertiesAndValidate<CreateEventAttribute>(db, property, value, context,
                        serviceProvider);
                }).ToList()
            };

            db.SaveChanges();

            foreach (object value in newValues)
            {
                property.Key.ExecuteHookMethods<CreateEventAttribute>(ModelStoreEventAttributeBase.EventType.After,
                    value, context, serviceProvider);
            }

            return response;
        }

        public static CreateResponse SetPropertiesAndValidate<TEventAttribute>(SapphireDbContext db,
            KeyValuePair<Type, string> property, object newValue,
            HttpInformation context, IServiceProvider serviceProvider)
            where TEventAttribute : ModelStoreEventAttributeBase
        {
            object newEntityObject = property.Key.SetFields(newValue);

            if (!ValidationHelper.ValidateModel(newEntityObject, serviceProvider,
                out Dictionary<string, List<string>> validationResults))
            {
                return new CreateResponse()
                {
                    Value = newEntityObject,
                    ValidationResults = validationResults
                };
            }

            property.Key.ExecuteHookMethods<TEventAttribute>(ModelStoreEventAttributeBase.EventType.Before,
                newEntityObject, context, serviceProvider);

            db.Add(newEntityObject);

            property.Key.ExecuteHookMethods<TEventAttribute>(ModelStoreEventAttributeBase.EventType.BeforeSave,
                newEntityObject, context, serviceProvider);

            return new CreateResponse()
            {
                Value = newEntityObject,
            };
        }
    }
}