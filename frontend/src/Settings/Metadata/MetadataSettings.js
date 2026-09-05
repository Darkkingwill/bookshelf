import React, { Component } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { icons, kinds } from 'Helpers/Props';
import SettingsToolbarConnector from 'Settings/SettingsToolbarConnector';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
// import MetadatasConnector from './Metadata/MetadatasConnector';
import MetadataProviderConnector from './MetadataProvider/MetadataProviderConnector';

class MetadataSettings extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this._saveCallback = null;

    this.state = {
      isSaving: false,
      hasPendingChanges: false,
      metadataProviders: [],
      providersFetched: false,
      providersFetching: false,
      testingProvider: null,
      testResults: {},
      showAddCustom: false,
      newCustomName: '',
      newCustomUrl: '',
      newCustomAuthToken: ''
    };
  }

  componentDidMount() {
    this.fetchProviders();
  }

  fetchProviders = () => {
    this.setState({ providersFetching: true });

    const { request } = createAjaxRequest({
      url: '/config/metadatasource',
      dataType: 'json'
    });

    request.then((data) => {
      this.setState({
        metadataProviders: data,
        initialMetadataProviders: JSON.stringify(data),
        providersFetched: true,
        providersFetching: false
      });
    }).fail(() => {
      this.setState({ providersFetching: false });
    });
  };

  onProviderToggle = (key) => {
    this.setState((prevState) => {
      const updated = prevState.metadataProviders.map((p) =>
        p.key === key ? { ...p, enabled: !p.enabled } : p
      );

      return {
        metadataProviders: updated,
        hasPendingChanges: JSON.stringify(updated) !== prevState.initialMetadataProviders
      };
    });
  };

  onProviderMove = (index, direction) => {
    this.setState((prevState) => {
      const providers = [...prevState.metadataProviders];
      const newIndex = index + direction;

      if (newIndex < 0 || newIndex >= providers.length) {
        return prevState;
      }

      const temp = providers[index];
      providers[index] = providers[newIndex];
      providers[newIndex] = temp;

      const updated = providers.map((p, i) => ({ ...p, priority: i }));

      return {
        metadataProviders: updated,
        hasPendingChanges: JSON.stringify(updated) !== prevState.initialMetadataProviders
      };
    });
  };

  onProviderSettingChange = (key, settingKey, value) => {
    this.setState((prevState) => {
      const updated = prevState.metadataProviders.map((p) =>
        p.key === key
          ? { ...p, settings: { ...p.settings, [settingKey]: value } }
          : p
      );

      return {
        metadataProviders: updated,
        hasPendingChanges: JSON.stringify(updated) !== prevState.initialMetadataProviders
      };
    });
  };

  onCustomFieldChange = (key, field, value) => {
    this.setState((prevState) => {
      const updated = prevState.metadataProviders.map((p) =>
        p.key === key ? { ...p, [field]: value } : p
      );

      return {
        metadataProviders: updated,
        hasPendingChanges: JSON.stringify(updated) !== prevState.initialMetadataProviders
      };
    });
  };

  onAddCustomProvider = () => {
    const { newCustomName, newCustomUrl, newCustomAuthToken } = this.state;

    if (!newCustomName.trim() || !newCustomUrl.trim()) {
      return;
    }

    const key = `custom_${newCustomName.trim().toLowerCase().replace(/[^a-z0-9]/g, '_')}_${Date.now()}`;

    this.setState((prevState) => {
      const updated = [
        ...prevState.metadataProviders,
        {
          key,
          displayName: newCustomName.trim(),
          enabled: true,
          priority: prevState.metadataProviders.length,
          requiresAuth: false,
          settings: {},
          isCustom: true,
          url: newCustomUrl.trim(),
          authToken: newCustomAuthToken.trim()
        }
      ];

      return {
        metadataProviders: updated,
        hasPendingChanges: true,
        showAddCustom: false,
        newCustomName: '',
        newCustomUrl: '',
        newCustomAuthToken: ''
      };
    });
  };

  onRemoveCustomProvider = (key) => {
    this.setState((prevState) => {
      const updated = prevState.metadataProviders
        .filter((p) => p.key !== key)
        .map((p, i) => ({ ...p, priority: i }));

      return {
        metadataProviders: updated,
        hasPendingChanges: true
      };
    });
  };

  onSaveProviders = () => {
    this.setState({ isSaving: true });

    const { request } = createAjaxRequest({
      url: '/config/metadatasource',
      method: 'PUT',
      dataType: 'json',
      data: JSON.stringify(this.state.metadataProviders)
    });

    request.then((data) => {
      this.setState({
        metadataProviders: data,
        initialMetadataProviders: JSON.stringify(data),
        isSaving: false,
        hasPendingChanges: false
      });
    }).fail(() => {
      this.setState({ isSaving: false });
    });
  };

  onTestProvider = (key) => {
    this.setState({ testingProvider: key });

    const { request } = createAjaxRequest({
      url: '/config/metadatasource/test',
      method: 'POST',
      dataType: 'json',
      data: JSON.stringify({ key })
    });

    request.then((data) => {
      this.setState((prevState) => ({
        testingProvider: null,
        testResults: { ...prevState.testResults, [key]: data.success }
      }));
    }).fail(() => {
      this.setState((prevState) => ({
        testingProvider: null,
        testResults: { ...prevState.testResults, [key]: false }
      }));
    });
  };

  //
  // Listeners

  onChildMounted = (saveCallback) => {
    this._saveCallback = saveCallback;
  };

  onChildStateChange = (payload) => {
    this.setState(payload);
  };

  onSavePress = () => {
    if (this._saveCallback) {
      this._saveCallback();
    }

    if (this.state.providersFetched) {
      this.onSaveProviders();
    }
  };

  //
  // Render

  renderProviderRow = (provider, index) => {
    const { testingProvider, testResults, metadataProviders } = this.state;
    const isTesting = testingProvider === provider.key;
    const testResult = testResults[provider.key];

    return (
      <div
        key={provider.key}
        style={{
          display: 'flex',
          alignItems: 'center',
          padding: '10px',
          marginBottom: '5px',
          backgroundColor: provider.enabled ? 'var(--tableBackgroundColor)' : 'var(--disabledBackgroundColor)',
          borderRadius: '4px',
          border: '1px solid var(--borderColor)'
        }}
      >
        <div style={{ display: 'flex', flexDirection: 'column', marginRight: '10px' }}>
          <Button
            kind={kinds.DEFAULT}
            size="small"
            isDisabled={index === 0}
            onPress={() => this.onProviderMove(index, -1)}
          >
            <Icon name={icons.ARROW_UP} />
          </Button>

          <Button
            kind={kinds.DEFAULT}
            size="small"
            isDisabled={index === metadataProviders.length - 1}
            onPress={() => this.onProviderMove(index, 1)}
          >
            <Icon name={icons.CARET_DOWN} />
          </Button>
        </div>

        <div style={{ flex: 1 }}>
          <div style={{ display: 'flex', alignItems: 'center', marginBottom: '5px' }}>
            <strong style={{ marginRight: '10px' }}>
              {provider.displayName}
            </strong>

            {provider.isCustom &&
              <span style={{
                fontSize: '11px',
                padding: '2px 6px',
                backgroundColor: 'var(--primaryColor)',
                borderRadius: '3px',
                color: 'white',
                marginRight: '5px'
              }}>
                Custom
              </span>
            }

            {provider.requiresAuth && !provider.isCustom &&
              <span style={{
                fontSize: '11px',
                padding: '2px 6px',
                backgroundColor: 'var(--warningColor)',
                borderRadius: '3px',
                color: 'white'
              }}>
                Requires API Key
              </span>
            }

            {testResult === true &&
              <Icon
                name={icons.CHECK}
                kind={kinds.SUCCESS}
                style={{ marginLeft: '10px' }}
              />
            }

            {testResult === false &&
              <Icon
                name={icons.DANGER}
                kind={kinds.DANGER}
                style={{ marginLeft: '10px' }}
              />
            }
          </div>

          {provider.isCustom && provider.enabled &&
            <div style={{ marginTop: '5px' }}>
              <div style={{ marginBottom: '5px' }}>
                <label style={{ fontSize: '12px', marginRight: '5px' }}>
                  URL:
                </label>
                <input
                  type="text"
                  value={provider.url || ''}
                  onChange={(e) => this.onCustomFieldChange(provider.key, 'url', e.target.value)}
                  placeholder="https://my-provider.example.com"
                  style={{
                    padding: '4px 8px',
                    border: '1px solid var(--borderColor)',
                    borderRadius: '3px',
                    width: '400px',
                    backgroundColor: 'var(--inputBackgroundColor)',
                    color: 'var(--textColor)'
                  }}
                />
              </div>
              <div>
                <label style={{ fontSize: '12px', marginRight: '5px' }}>
                  Authorization:
                </label>
                <input
                  type="password"
                  value={provider.authToken || ''}
                  onChange={(e) => this.onCustomFieldChange(provider.key, 'authToken', e.target.value)}
                  placeholder="Optional auth token"
                  style={{
                    padding: '4px 8px',
                    border: '1px solid var(--borderColor)',
                    borderRadius: '3px',
                    width: '300px',
                    backgroundColor: 'var(--inputBackgroundColor)',
                    color: 'var(--textColor)'
                  }}
                />
              </div>
            </div>
          }

          {provider.key === 'audible' && provider.enabled && !provider.isCustom &&
            <div style={{ marginTop: '5px' }}>
              <label style={{ fontSize: '12px', marginRight: '5px' }}>
                Region:
              </label>
              <select
                value={(provider.settings && provider.settings.region) || 'us'}
                onChange={(e) => this.onProviderSettingChange(provider.key, 'region', e.target.value)}
                style={{
                  padding: '4px 8px',
                  border: '1px solid var(--borderColor)',
                  borderRadius: '3px',
                  backgroundColor: 'var(--inputBackgroundColor)',
                  color: 'var(--textColor)'
                }}
              >
                <option value="us">United States (.com)</option>
                <option value="uk">United Kingdom (.co.uk)</option>
                <option value="ca">Canada (.ca)</option>
                <option value="au">Australia (.com.au)</option>
                <option value="de">Germany (.de)</option>
                <option value="fr">France (.fr)</option>
                <option value="it">Italy (.it)</option>
                <option value="es">Spain (.es)</option>
                <option value="jp">Japan (.co.jp)</option>
                <option value="in">India (.in)</option>
              </select>
              <span style={{ fontSize: '11px', marginLeft: '8px', color: 'var(--disabledColor)' }}>
                Searches Audible catalog + Audnexus for rich metadata. No API key needed.
              </span>
            </div>
          }

          {provider.key === 'googlebooks' && provider.enabled && !provider.isCustom &&
            <div style={{ marginTop: '5px' }}>
              <label style={{ fontSize: '12px', marginRight: '5px' }}>
                API Key:
              </label>
              <input
                type="text"
                value={(provider.settings && provider.settings.apiKey) || ''}
                onChange={(e) => this.onProviderSettingChange(provider.key, 'apiKey', e.target.value)}
                placeholder="Required - get free key at console.cloud.google.com"
                style={{
                  padding: '4px 8px',
                  border: '1px solid var(--borderColor)',
                  borderRadius: '3px',
                  width: '350px',
                  backgroundColor: 'var(--inputBackgroundColor)',
                  color: 'var(--textColor)'
                }}
              />
              <span style={{ fontSize: '11px', marginLeft: '8px', color: 'var(--disabledColor)' }}>
                Required. Free tier: 1,000 req/day at console.cloud.google.com.
              </span>
            </div>
          }

          {provider.key === 'hardcover' && provider.enabled && !provider.isCustom &&
            <div style={{ marginTop: '5px' }}>
              <label style={{ fontSize: '12px', marginRight: '5px' }}>
                API Token:
              </label>
              <input
                type="text"
                value={(provider.settings && provider.settings.apiToken) || ''}
                onChange={(e) => this.onProviderSettingChange(provider.key, 'apiToken', e.target.value)}
                placeholder="Enter your Hardcover API token"
                style={{
                  padding: '4px 8px',
                  border: '1px solid var(--borderColor)',
                  borderRadius: '3px',
                  width: '300px',
                  backgroundColor: 'var(--inputBackgroundColor)',
                  color: 'var(--textColor)'
                }}
              />
            </div>
          }

          {provider.key === 'rreadingglasses' && provider.enabled && !provider.isCustom &&
            <div style={{ marginTop: '5px' }}>
              <label style={{ fontSize: '12px', marginRight: '5px' }}>
                Base URL:
              </label>
              <input
                type="text"
                value={(provider.settings && provider.settings.baseUrl) || ''}
                onChange={(e) => this.onProviderSettingChange(provider.key, 'baseUrl', e.target.value)}
                placeholder="Default: https://api.bookinfo.pro"
                style={{
                  padding: '4px 8px',
                  border: '1px solid var(--borderColor)',
                  borderRadius: '3px',
                  width: '300px',
                  backgroundColor: 'var(--inputBackgroundColor)',
                  color: 'var(--textColor)'
                }}
              />
              <div style={{ fontSize: '11px', marginTop: '3px', color: 'var(--disabledColor)' }}>
                Uses api.bookinfo.pro by default. Set a URL here to use a self-hosted instance instead.
              </div>
            </div>
          }
        </div>

        <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
          <Button
            kind={kinds.DEFAULT}
            size="small"
            isDisabled={isTesting}
            onPress={() => this.onTestProvider(provider.key)}
          >
            {isTesting ? 'Testing...' : 'Test'}
          </Button>

          {provider.isCustom &&
            <Button
              kind={kinds.DANGER}
              size="small"
              onPress={() => this.onRemoveCustomProvider(provider.key)}
            >
              <Icon name={icons.DELETE} />
            </Button>
          }

          <Button
            kind={provider.enabled ? kinds.SUCCESS : kinds.DANGER}
            size="small"
            onPress={() => this.onProviderToggle(provider.key)}
          >
            {provider.enabled ? 'Enabled' : 'Disabled'}
          </Button>
        </div>
      </div>
    );
  };

  renderAddCustomForm = () => {
    const { showAddCustom, newCustomName, newCustomUrl, newCustomAuthToken } = this.state;

    if (!showAddCustom) {
      return (
        <div style={{ marginTop: '15px' }}>
          <Button
            kind={kinds.PRIMARY}
            onPress={() => this.setState({ showAddCustom: true })}
          >
            <Icon name={icons.ADD} />
            {' Add Custom Provider'}
          </Button>
        </div>
      );
    }

    return (
      <div style={{
        marginTop: '15px',
        padding: '15px',
        border: '1px solid var(--borderColor)',
        borderRadius: '4px',
        backgroundColor: 'var(--tableBackgroundColor)'
      }}>
        <h4 style={{ marginTop: 0, marginBottom: '10px' }}>Add Custom Metadata Provider</h4>
        <Alert kind={kinds.INFO} style={{ marginBottom: '10px' }}>
          Custom providers must implement the
          {' '}
          <a
            href="https://audiobookshelf.org/docs/documentation/community/community-providers/"
            target="_blank"
            rel="noopener noreferrer"
          >
            Audiobookshelf custom provider specification
          </a>
          . The provider should respond to GET /search?query=... with a JSON object containing a "matches" array.
        </Alert>
        <div style={{ marginBottom: '10px' }}>
          <label style={{ display: 'block', fontSize: '12px', marginBottom: '3px' }}>
            Name *
          </label>
          <input
            type="text"
            value={newCustomName}
            onChange={(e) => this.setState({ newCustomName: e.target.value })}
            placeholder="My Custom Provider"
            style={{
              padding: '6px 8px',
              border: '1px solid var(--borderColor)',
              borderRadius: '3px',
              width: '300px',
              backgroundColor: 'var(--inputBackgroundColor)',
              color: 'var(--textColor)'
            }}
          />
        </div>
        <div style={{ marginBottom: '10px' }}>
          <label style={{ display: 'block', fontSize: '12px', marginBottom: '3px' }}>
            URL *
          </label>
          <input
            type="text"
            value={newCustomUrl}
            onChange={(e) => this.setState({ newCustomUrl: e.target.value })}
            placeholder="https://my-provider.example.com"
            style={{
              padding: '6px 8px',
              border: '1px solid var(--borderColor)',
              borderRadius: '3px',
              width: '400px',
              backgroundColor: 'var(--inputBackgroundColor)',
              color: 'var(--textColor)'
            }}
          />
        </div>
        <div style={{ marginBottom: '15px' }}>
          <label style={{ display: 'block', fontSize: '12px', marginBottom: '3px' }}>
            Authorization (optional)
          </label>
          <input
            type="password"
            value={newCustomAuthToken}
            onChange={(e) => this.setState({ newCustomAuthToken: e.target.value })}
            placeholder="Bearer token or API key"
            style={{
              padding: '6px 8px',
              border: '1px solid var(--borderColor)',
              borderRadius: '3px',
              width: '300px',
              backgroundColor: 'var(--inputBackgroundColor)',
              color: 'var(--textColor)'
            }}
          />
        </div>
        <div style={{ display: 'flex', gap: '8px' }}>
          <Button
            kind={kinds.PRIMARY}
            isDisabled={!newCustomName.trim() || !newCustomUrl.trim()}
            onPress={this.onAddCustomProvider}
          >
            Add Provider
          </Button>
          <Button
            kind={kinds.DEFAULT}
            onPress={() => this.setState({
              showAddCustom: false,
              newCustomName: '',
              newCustomUrl: '',
              newCustomAuthToken: ''
            })}
          >
            Cancel
          </Button>
        </div>
      </div>
    );
  };

  render() {
    const {
      isSaving,
      hasPendingChanges,
      metadataProviders,
      providersFetched,
      providersFetching
    } = this.state;

    return (
      <PageContent title="Metadata Sources">
        <SettingsToolbarConnector
          isSaving={isSaving}
          hasPendingChanges={hasPendingChanges}
          onSavePress={this.onSavePress}
        />

        <PageContentBody>
          <FieldSet legend="Metadata Search Providers">
            <Alert kind={kinds.INFO}>
              Configure which metadata sources are used when searching for books and authors.
              Providers are tried in order from top to bottom.
              Google Books and Open Library work out of the box with no configuration.
            </Alert>

            {providersFetching &&
              <LoadingIndicator />
            }

            {providersFetched &&
              <div style={{ marginTop: '15px' }}>
                {metadataProviders.map((provider, index) =>
                  this.renderProviderRow(provider, index)
                )}
              </div>
            }

            {providersFetched && this.renderAddCustomForm()}
          </FieldSet>

          <MetadataProviderConnector
            onChildMounted={this.onChildMounted}
            onChildStateChange={this.onChildStateChange}
          />
          {/* <MetadatasConnector /> */}
        </PageContentBody>
      </PageContent>
    );
  }
}

export default MetadataSettings;
